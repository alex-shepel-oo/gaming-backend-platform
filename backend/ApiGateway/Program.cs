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

builder.Services.AddOptions<PlayerClientCorsOptions>()
    .Bind(builder.Configuration.GetSection(PlayerClientCorsOptions.SectionName))
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

builder.Services.AddHealthChecks();
builder.Services.AddOcelot(builder.Configuration).AddConsul<ServiceAddressConsulServiceBuilder>().AddPolly();

var app = builder.Build();

app.UseCors(PlayerClientCorsPolicy);

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
