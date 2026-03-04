using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SoccerAi.Application.Interfaces;
using SoccerAi.Infrastructure.Persistence;
using Mediator.Net;
using SoccerAi.Application.Features.Automation;

namespace SoccerAi.IntegrationTests;

[Collection("IntegrationTests")]
public class SyncIntegrationTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly IServiceProvider _services;

    public SyncIntegrationTests(CustomWebApplicationFactory<Program> factory)
    {
        // Mock external API to avoid hitting real limits during tests
        var apiFootballMock = new Mock<IApiFootballService>();
        apiFootballMock.Setup(x => x.GetFixturesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new List<SoccerAi.Application.Interfaces.ApiFixture>());
        apiFootballMock.Setup(x => x.GetStandingsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SoccerAi.Application.Entities.Team>());
        
        // Use factory but replace specific services
        _services = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IApiFootballService));
                if (descriptor != null) services.Remove(descriptor);
                services.AddSingleton(apiFootballMock.Object);
            });
        }).Services;
    }

    [Fact]
    public void Mediator_IsResolvable()
    {
        using var scope = _services.CreateScope();
        var mediator = scope.ServiceProvider.GetService<IMediator>();
        mediator.Should().NotBeNull();
    }

    [Fact]
    public async Task RunNightlySync_CompletesWithoutErrors()
    {
        using var scope = _services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        
        Func<Task> act = async () => await mediator.SendAsync(new RunDailySyncCommand(2025), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunNativeMlTraining_CompletesWithoutErrors()
    {
        using var scope = _services.CreateScope();
        var trainer = scope.ServiceProvider.GetRequiredService<IMlTrainingService>();
        
        Func<Task> act = async () => await trainer.TrainModelsAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
