using Microsoft.EntityFrameworkCore;
using soccer_gpt_infrastructure.Persistence;
using soccer_gpt_infrastructure.Services;

// Quick test to sync Bundesliga 2024
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json");

// Add services
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=/Users/shivm/Workspace/soccer-gpt-api/soccer.db;Foreign Keys=False"));

builder.Services.AddHttpClient<soccer_gpt_application.Interfaces.IApiFootballService, ApiFootballService>(client =>
{
    var config = builder.Configuration.GetSection("ApiFootball");
    client.BaseAddress = new Uri(config["BaseUrl"]!);
    client.DefaultRequestHeaders.Add("x-apisports-key", config["ApiKey"]);
});

builder.Services.AddSingleton<soccer_gpt_application.Interfaces.IHistoricalDataService, HistoricalDataService>();
builder.Services.AddScoped<soccer_gpt_application.Interfaces.IApplicationDbContext>(p => p.GetRequiredService<ApplicationDbContext>());
builder.Services.AddScoped<FixtureSyncService>();
builder.Services.AddLogging(l => l.AddConsole());

var app = builder.Build();

using var scope = app.Services.CreateScope();
var syncService = scope.ServiceProvider.GetRequiredService<FixtureSyncService>();

Console.WriteLine("Testing Bundesliga (78) sync for season 2024...");
var result = await syncService.SyncLeagueFixturesAsync(78, 2024, CancellationToken.None);
Console.WriteLine($"Result: Created={result.Created}, Updated={result.Updated}, Errors={result.Errors}");
