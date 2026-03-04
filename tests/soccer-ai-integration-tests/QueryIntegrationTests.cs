using FluentAssertions;
using Mediator.Net;
using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Application.Features.Predictions;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Features.Backtesting;
using SoccerAi.Application.Features.Analysis;

namespace SoccerAi.IntegrationTests;

[Collection("IntegrationTests")]
public class QueryIntegrationTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly IServiceProvider _services;

    public QueryIntegrationTests(CustomWebApplicationFactory<Program> factory)
    {
        _services = factory.Services;
    }

    [Fact]
    public async Task GetFixturePredictionsQuery_ReturnsValidResponse()
    {
        using var scope = _services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var query = new GetFixturePredictionsQuery { Date = DateTimeOffset.Parse("2025-01-01"), LeagueId = 39, Language = "en" };
        var result = await mediator.RequestAsync<GetFixturePredictionsQuery, GetFixturePredictionsResponse>(query, CancellationToken.None);
        result.Should().NotBeNull();
    }
    
    [Fact]
    public async Task GetMatchCombinationQuery_ReturnsValidResponse()
    {
        using var scope = _services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var query = new GetMatchCombinationQuery(DateTimeOffset.UtcNow, "en");
        var result = await mediator.RequestAsync<GetMatchCombinationQuery, GetMatchCombinationResponse>(query, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetBacktestReportQuery_ReturnsValidResponse()
    {
        using var scope = _services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var query = new GetBacktestReportQuery(10, 25.0);
        var result = await mediator.RequestAsync<GetBacktestReportQuery, GetBacktestReportResponse>(query, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMatchAnalysisQuery_ReturnsValidResponse()
    {
        using var scope = _services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var query = new GetMatchAnalysisQuery { Date = DateTimeOffset.UtcNow, Language = "en" };
        var result = await mediator.RequestAsync<GetMatchAnalysisQuery, GetMatchAnalysisResponse>(query, CancellationToken.None);
        result.Should().NotBeNull();
    }
}
