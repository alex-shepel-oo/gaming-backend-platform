using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using BuildingBlocks.Messaging;
using IdentityService.Auth;
using IdentityService.Infrastructure;
using IdentityService.Options;
using IdentityService.Persistence;
using IdentityService.RateLimiting;
using IdentityService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("IdentityDb")));

        return services;
    }

    public static IServiceCollection AddIdentityHealthChecks(this IServiceCollection services)
    {
        // Connection string is read lazily from IConfiguration at check-execution time, the
        // same way AddDbContext defers it -- reading it eagerly here would capture whatever
        // IConfiguration held at service-registration time, missing overrides (e.g. in tests)
        // that land afterwards.
        services.AddHealthChecks()
            .AddNpgSql(
                sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("IdentityDb")!,
                name: "postgresql",
                tags: ["ready"])
            .AddRabbitMQ(
                sp => sp.GetRequiredService<IRabbitMqConnection>().GetConnectionAsync(),
                name: "rabbitmq",
                tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddIdentityExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    public static IServiceCollection AddIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RefreshTokenOptions>()
            .Bind(configuration.GetSection(RefreshTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailVerificationOptions>()
            .Bind(configuration.GetSection(EmailVerificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RefreshCookieOptions>()
            .Bind(configuration.GetSection(RefreshCookieOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AdminRefreshCookieOptions>()
            .Bind(configuration.GetSection(AdminRefreshCookieOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PasswordResetOptions>()
            .Bind(configuration.GetSection(PasswordResetOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SeedingOptions>()
            .Bind(configuration.GetSection(SeedingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ApiOptions>()
            .Bind(configuration.GetSection(ApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IJwtSigningKeys, JwtSigningKeys>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddSingleton<ICookieAuthWriter, CookieAuthWriter>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IVerificationCodeGenerator, VerificationCodeGenerator>();
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IPermissionResolver, PermissionResolver>();
        services.AddScoped<IScopeAuthorityGuard, ScopeAuthorityGuard>();

        return services;
    }

    public static IServiceCollection AddIdentityAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, IJwtSigningKeys>((bearerOptions, jwtOptions, signingKeys) =>
            {
                var options = jwtOptions.Value;

                // Both AddJwtBearer's default handler and TokenService issue standard
                // short claim names (sub, email, jti); mapping them to legacy XML schema
                // URIs would silently break every claim read through IdentityClaims/ICurrentUser.
                bearerOptions.MapInboundClaims = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudiences = options.Audiences,
                    IssuerSigningKey = signingKeys.SigningKey,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                };

                bearerOptions.Events = new JwtBearerEvents { OnTokenValidated = RejectRevokedAccessTokenAsync };
            });

        services.AddAuthorization(AuthorizationPolicies.Configure);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }

    public static IServiceCollection AddIdentityRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiterOptions.OnRejected = WriteRateLimitProblemAsync;

            limiterOptions.AddPolicy(RateLimitPolicies.Login, IpPartition(o => (o.LoginPermitLimit, o.LoginWindowSeconds)));
            limiterOptions.AddPolicy(RateLimitPolicies.Register, IpPartition(o => (o.RegisterPermitLimit, o.RegisterWindowSeconds)));
            limiterOptions.AddPolicy(RateLimitPolicies.ConfirmEmail, IpPartition(o => (o.ConfirmEmailPermitLimit, o.ConfirmEmailWindowSeconds)));
            limiterOptions.AddPolicy(
                RateLimitPolicies.ResendVerification, IpPartition(o => (o.ResendVerificationPermitLimit, o.ResendVerificationWindowSeconds)));
            limiterOptions.AddPolicy(
                RateLimitPolicies.RequestPasswordReset,
                IpPartition(o => (o.RequestPasswordResetPermitLimit, o.RequestPasswordResetWindowSeconds)));
            limiterOptions.AddPolicy(
                RateLimitPolicies.ResetPassword,
                IpPartition(o => (o.ResetPasswordPermitLimit, o.ResetPasswordWindowSeconds)));
        });

        return services;
    }

    // Only FrontendBaseUrl lives here now -- actually sending email moved to EmailService, reached
    // through the three outbox events written by EmailVerificationService/PasswordResetService/
    // AuthenticationService, not a direct call from this service.
    public static IServiceCollection AddIdentityEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    private static Func<HttpContext, RateLimitPartition<string>> IpPartition(
        Func<RateLimitingOptions, (int PermitLimit, int WindowSeconds)> selectLimit) =>
        httpContext =>
        {
            var options = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
            var (permitLimit, windowSeconds) = selectLimit(options);
            var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
            });
        };

    private static async ValueTask WriteRateLimitProblemAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var problemDetailsService = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "Rate limit exceeded. Try again later.",
            },
        });
    }

    private static async Task RejectRevokedAccessTokenAsync(TokenValidatedContext context)
    {
        var jtiClaim = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);

        if (jtiClaim is null || !Guid.TryParse(jtiClaim, out var jti))
        {
            context.Fail("Token has no jti claim.");
            return;
        }

        var dbContext = context.HttpContext.RequestServices.GetRequiredService<IdentityDbContext>();
        var isRevoked = await dbContext.RevokedAccessTokens.AnyAsync(t => t.Jti == jti);

        if (isRevoked)
        {
            context.Fail("Access token has been revoked.");
        }
    }
}
