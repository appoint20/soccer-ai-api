using soccer_gpt_application;
using soccer_gpt_infrastructure;
using Mediator.Net;
using Mediator.Net.MicrosoftDependencyInjection;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

// Mediator.Net Configuration
var mediaBuilder = new MediatorBuilder();
mediaBuilder.RegisterHandlers(typeof(soccer_gpt_application.Features.Leagues.Queries.GetLeaguesQueryHandler).Assembly);
builder.Services.RegisterMediator(mediaBuilder);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
