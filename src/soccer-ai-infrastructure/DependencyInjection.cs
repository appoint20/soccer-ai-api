using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using SoccerAi.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Polly;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;
using SoccerAi.Infrastructure.MlNet;

namespace SoccerAi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Model constants (typed options; defaults apply when section absent)
        services.Configure<SoccerAi.Application.Options.DixonColesOptions>(
            configuration.GetSection(SoccerAi.Application.Options.DixonColesOptions.SectionName));

        services.AddPersistence(configuration);
        services.AddExternalApis(configuration);
        
        RegisterAiAnalysisService(services, configuration);

        services.AddServices();
        
        return services;
    }

    private static void AddServices(this IServiceCollection services)
    {
        // Core Analysis & Prediction
        services.AddScoped<IMatchAnalysisService, MatchAnalysisService>();
        services.AddScoped<IMatchDataProvider, MatchDataProvider>();
        services.AddScoped<IFixtureSyncService, FixtureSyncService>();
        services.AddScoped<IAiSyncService, AiSyncService>();
        services.AddScoped<ITeamSyncService, TeamSyncService>();
        
        // Mathematical Engines
        services.AddScoped<IProbabilityPipeline, ProbabilityPipeline>();
        services.AddScoped<IProbabilityConsensusEngine, ProbabilityConsensusEngine>();
        services.AddScoped<DecisionService>(); // Register concretely for AiDecisionService to use
        services.AddScoped<IDecisionService, AiDecisionService>(); // AI-driven implementation

        services.AddScoped<ILeagueAdjustmentService, LeagueAdjustmentService>();
        services.AddScoped<ILeagueVolatilityService, LeagueVolatilityService>();
        services.AddScoped<ITrapDetectionService, TrapDetectionService>();
        services.AddScoped<IFeatureExtractionService, FeatureExtractionService>();
        services.AddScoped<IFeatureScoringEngine, FeatureScoringEngine>();
        services.AddScoped<IExpectedValueEngine, ExpectedValueEngine>();
        services.AddScoped<IMarketCalibrationService, MarketCalibrationServiceImpl>();
        
        // Machine Learning
        services.AddScoped<IMlPredictionService, MlPredictionService>();
        services.AddScoped<IMlTrainingService, MlTrainingService>();
        services.AddSingleton<MlTrainingDataBuilder>();

        // Security & Utilities
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<INlpService, NlpService>();

        // Background Automation
        services.AddHostedService<DailySyncBackgroundService>();
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IApplicationDbContext>(provider => 
            provider.GetRequiredService<ApplicationDbContext>());
    }

    private static void AddExternalApis(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IApiFootballService, ApiFootballService>((provider, client) =>
        {
            var options = configuration.GetSection("ApiFootball").Get<FootballApiOptions>();
            var apiKey = Environment.GetEnvironmentVariable("API_FOOTBALL_KEY") ?? options?.ApiKey;

            client.BaseAddress = new Uri(options?.BaseUrl ?? "https://v3.football.api-sports.io");
            client.DefaultRequestHeaders.Add("x-apisports-key", apiKey);
        })
        .AddResilienceHandler("football-api", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2)
            });
            builder.AddTimeout(TimeSpan.FromSeconds(30));
        });
    }

    private static void RegisterAiAnalysisService(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiServiceOptions>(configuration.GetSection("AiService"));
        services.AddScoped<IAiAnalysisService, OpenAiAnalysisService>();
        
        services.AddScoped<ChatClient>(sp => 
        {
            var options = sp.GetRequiredService<IOptions<AiServiceOptions>>().Value;
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException(
                    "AI Service API Key is missing. Please ensure 'AiService:ApiKey' is configured in appsettings.json " +
                    "or set the 'AiService__ApiKey' environment variable in your hosting environment (e.g., Render).");
            }

            var clientOptions = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl.TrimEnd('/') + "/") };
            return new ChatClient(options.DefaultModel ?? "glm-4-plus", new System.ClientModel.ApiKeyCredential(options.ApiKey), clientOptions);
        });
    }

}
