using soccer_gpt_infrastructure;
using soccer_gpt_worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<soccer_gpt_infrastructure.Services.PredictionWorkerService>();

var host = builder.Build();
host.Run();
