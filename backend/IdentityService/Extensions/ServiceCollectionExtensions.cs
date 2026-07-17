using System.Security.Claims;
using System.Text;
using IdentityService.Auth;
using IdentityService.Infrastructure;
using IdentityService.Options;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Services.Email;
using IdentityService.Services.Email.Templates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IVerificationCodeGenerator, VerificationCodeGenerator>();
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<ISessionService, SessionService>();

        return services;
    }

    public static IServiceCollection AddIdentityAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
            {
                var options = jwtOptions.Value;

                // Both AddJwtBearer's default handler and TokenService issue standard
                // short claim names (sub, email, jti); mapping them to legacy XML schema
                // URIs would silently break every claim read through IdentityClaims/ICurrentUser.
                bearerOptions.MapInboundClaims = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
                    ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                };

                bearerOptions.Events = new JwtBearerEvents { OnTokenValidated = RejectRevokedAccessTokenAsync };
            });

        services.AddAuthorization(AuthorizationPolicies.Configure);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }

    public static IServiceCollection AddIdentityEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        var provider = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()?.Provider;

        if (string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, NoopEmailSender>();
        }

        return services;
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
