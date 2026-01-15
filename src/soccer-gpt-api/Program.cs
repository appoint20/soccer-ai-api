using Mediator.Net;
using Mediator.Net.MicrosoftDependencyInjection;
using Scalar.AspNetCore;
using soccer_gpt_application;
using soccer_gpt_application.Features.HistoricalMatches.Query;
using soccer_gpt_infrastructure;
using soccer_gpt_infrastructure.Persistence;

// Register Encoding Provider for ExcelDataReader
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi("v1");

// Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// Mediator.Net Configuration
var mediaBuilder = new MediatorBuilder();
mediaBuilder.RegisterHandlers(typeof(GetHistoricalMatchesQuery).Assembly);
builder.Services.RegisterMediator(mediaBuilder);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("Soccer GPT API")
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseAuthorization();
app.MapControllers();

// Ensure Database Created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.EnsureCreated();
}

app.Run();
