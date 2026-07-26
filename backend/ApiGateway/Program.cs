using System.Globalization;
using System.Text;
using ApiGateway.Options;
using ApiGateway.ServiceDiscovery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using Ocelot.Provider.Consul;
using Ocelot.Provider.Polly;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: false)
    .AddJsonFile($"ocelot.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false);

builder.Host.UseSerilog((context, configuration) => configuration
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
    {
        var options = jwtOptions.Value;

        bearerOptions.MapInboundClaims = false;
        bearerOptions.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudiences = options.Audiences,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
            ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
        };
    });

const string PlayerClientCorsPolicy = "PlayerClientCors";
const string AdminClientCorsPolicy = "AdminClientCors";

builder.Services.AddOptions<PlayerClientCorsOptions>()
    .Bind(builder.Configuration.GetSection(PlayerClientCorsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// AdminCors:AllowedOrigins in appsettings.json is a dev-shaped placeholder
// (admin-client's future Nginx/ng-serve ports, one above player-client's own
// 8080/4200) -- Session 7's infra commit swaps these for the real deployed
// admin-client origin.
builder.Services.AddOptions<AdminClientCorsOptions>()
    .Bind(builder.Configuration.GetSection(AdminClientCorsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<PlayerClientCorsOptions>>((corsOptions, playerClientCorsOptions) =>
        corsOptions.AddPolicy(PlayerClientCorsPolicy, policy => policy
            .WithOrigins(playerClientCorsOptions.Value.AllowedOrigins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithHeaders("Content-Type", "Authorization", "X-Client-Type")
            .AllowCredentials()));
builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<AdminClientCorsOptions>>((corsOptions, adminClientCorsOptions) =>
        corsOptions.AddPolicy(AdminClientCorsPolicy, policy => policy
            .WithOrigins(adminClientCorsOptions.Value.AllowedOrigins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithHeaders("Content-Type", "Authorization", "X-Client-Type")
            .AllowCredentials()));

builder.Services.AddHealthChecks();
builder.Services.AddOcelot(builder.Configuration).AddConsul<ServiceAddressConsulServiceBuilder>().AddPolly();

var app = builder.Build();

// Ocelot has no per-route CORS of its own, so the admin/player split is done
// with two explicit UseWhen branches instead of one blanket UseCors call.
// The predicates are each other's negation on purpose - UseWhen branches
// are not mutually exclusive by default, so both cases need to be spelled
// out to avoid a path matching neither (or, worse, both) policies.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api/admin"),
    branch => branch.UseCors(AdminClientCorsPolicy));
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api/admin"),
    branch => branch.UseCors(PlayerClientCorsPolicy));

app.UseHealthChecks("/health");

// Ocelot's own middleware is terminal for unmatched paths, so anything served
// by endpoint routing (Scalar's UI) has to be dispatched here, ahead of
// UseOcelot -- otherwise Ocelot answers 404 before routing ever sees it. That
// requires explicit UseEndpoints instead of top-level route registration.
#pragma warning disable ASP0014
app.UseRouting();
app.UseEndpoints(endpoints => endpoints.MapScalarApiReference(options =>
    options.AddDocument("identity", "Identity API", "/openapi/identity/v1.json", isDefault: true)));
#pragma warning restore ASP0014

await app.UseOcelot();

app.Run();

public partial class Program;
