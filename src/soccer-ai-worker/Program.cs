using Mediator.Net;
using Mediator.Net.MicrosoftDependencyInjection;
using SoccerAi.Application;
using SoccerAi.Infrastructure;
using SoccerAi.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Mediator (some sync services publish through handlers)
var mediatorBuilder = new MediatorBuilder();
mediatorBuilder.RegisterHandlers(
    typeof(SoccerAi.Application.Features.Analysis.GetMatchAnalysisHandler).Assembly);
builder.Services.RegisterMediator(mediatorBuilder);

// Worker
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.AddSingleton<SyncPipeline>();
builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddHostedService<OddsCaptureWorker>();

var host = builder.Build();
await host.RunAsync();
