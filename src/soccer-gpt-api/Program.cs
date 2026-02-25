using Mediator.Net;
using Mediator.Net.MicrosoftDependencyInjection;
using Scalar.AspNetCore;
using soccer_gpt_application;
using soccer_gpt_infrastructure;
using soccer_gpt_infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// Register Encoding Provider for ExcelDataReader
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

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

// Mediator.Net Configuration
var mediaBuilder = new MediatorBuilder();
mediaBuilder.RegisterHandlers(typeof(soccer_gpt_application.Features.Predictions.GetFixturePredictionsHandler).Assembly);
builder.Services.RegisterMediator(mediaBuilder);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Soccer GPT API")
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

app.UseCors("AllowAll");
app.UseAuthorization();

// Global Exception Handler
app.UseMiddleware<soccer_gpt_api.Middleware.GlobalExceptionMiddleware>();

app.MapControllers();

// Ensure Database Created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.Migrate();
}

app.Run();

