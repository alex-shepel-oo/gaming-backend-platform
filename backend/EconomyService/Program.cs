using System.Globalization;
using EconomyService.Endpoints;
using EconomyService.Extensions;
using EconomyService.Persistence;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddEconomyExceptionHandling();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddEconomyPersistence(builder.Configuration);
builder.Services.AddScoped<DevelopmentSeeder>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
        await seeder.SeedAsync();
    }
}

app.MapHealthEndpoints();

app.Run();

public partial class Program;
