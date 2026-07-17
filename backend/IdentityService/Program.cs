using System.Globalization;
using IdentityService.Endpoints;
using IdentityService.Extensions;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddOpenApi();
builder.Services.AddIdentityPersistence(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthEndpoints();

app.Run();

public partial class Program;
