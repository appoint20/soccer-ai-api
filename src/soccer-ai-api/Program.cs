using System;
using System.Linq;
using Mediator.Net;
using SoccerAi.Application.Features.Backtesting;
using Mediator.Net.MicrosoftDependencyInjection;
using Scalar.AspNetCore;
using SoccerAi.Application;
using SoccerAi.Infrastructure;
using SoccerAi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Infrastructure.Services;
using SoccerAi.Api.Configuration;
using SoccerAi.Api.Security;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddOpenApi("v1");

// Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IJwtService, JwtService>();

builder.Services.AddOptions<AdminApiKeyOptions>()
    .Bind(builder.Configuration.GetSection(AdminApiKeyOptions.SectionName));

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "soccer-ai-api",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "soccer-ai-api",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"] ?? "D0A9C3E1F4B5A6D7C8E9F0A1B2C3D4E5F6A7B8C9D0E1F2A3B4C5D6E7F8A9B0C1"))
        };
    })
    .AddScheme<AuthenticationSchemeOptions, AdminApiKeyAuthenticationHandler>(
        AdminApiKeyAuthenticationDefaults.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CombinedPolicy", policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, AdminApiKeyAuthenticationDefaults.SchemeName);
        policy.RequireAuthenticatedUser();
    });

    options.AddPolicy("JwtPolicy", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });

    options.AddPolicy(AdminApiKeyAuthenticationDefaults.PolicyName, policy =>
    {
        policy.AddAuthenticationSchemes(AdminApiKeyAuthenticationDefaults.SchemeName);
        policy.RequireAuthenticatedUser();
    });
});

// Mediator.Net Configuration
var mediaBuilder = new MediatorBuilder();
mediaBuilder.RegisterHandlers(typeof(SoccerAi.Application.Features.Analysis.GetMatchAnalysisHandler).Assembly);
builder.Services.RegisterMediator(mediaBuilder);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Soccer AI API")
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

// Global exception middleware should run before auth handlers
app.UseMiddleware<SoccerAi.Api.Middleware.GlobalExceptionMiddleware>();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Ensure Database Created
using (var scope = app.Services.CreateScope())
{
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogCritical(ex, "Failed to initialize database context.");
    }
}

if (args.Contains("--backtest"))
{
    Console.WriteLine("Starting Native ML.NET 10-Week Backtest Pipeline...");
    using var scope = app.Services.CreateScope();
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
    var response = await mediator.RequestAsync<GetBacktestReportQuery, GetBacktestReportResponse>(
        new GetBacktestReportQuery(10, 1.0));
    var json = System.Text.Json.JsonSerializer.Serialize(response, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    await System.IO.File.WriteAllTextAsync("backtest_result.json", json);
    Console.WriteLine($"Backtest Complete! JSON written to backtest_result.json");
    return;
}

if (args.Contains("--ml"))
{
    Console.WriteLine("Starting Native ML.NET Model Training Pipeline...");
    using var scope = app.Services.CreateScope();
    var mlService = scope.ServiceProvider.GetRequiredService<IMlTrainingService>();
    await mlService.TrainModelsAsync();
    Console.WriteLine("ML Training Complete!");
    return;
}

if (args.Contains("--sync-gemini"))
{
    var fixtureIdArg = args.FirstOrDefault(a => a.StartsWith("--fixture-id="));
    int? fixtureId = fixtureIdArg != null ? int.Parse(fixtureIdArg.Split('=')[1]) : null;

    Console.WriteLine(fixtureId.HasValue 
        ? $"Starting Gemini Analysis Sync for Fixture {fixtureId}..." 
        : "Starting Gemini Analysis Batch Sync...");

    using var scope = app.Services.CreateScope();
    var geminiSyncService = scope.ServiceProvider.GetRequiredService<IGeminiSyncService>();
    
    if (fixtureId.HasValue)
        await geminiSyncService.SyncSingleFixtureAsync(fixtureId.Value);
    else
    {
        var analysisService = scope.ServiceProvider.GetRequiredService<IMatchAnalysisService>();
        var fixtures = await analysisService.AnalyzeLatestFixtureByAsync(DateTime.Now);
        await geminiSyncService.SyncUpcomingFixturesAsync(fixtures);
    }

    Console.WriteLine("Gemini Sync Complete!");
    return;
}

app.Run();

public partial class Program { }
