using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SoccerAi.Application.Interfaces;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// The provider's prediction endpoint is the one source of match evidence that
/// needs no bookmaker: outcome percentages, a home-versus-away comparison, and
/// each side's recent scoring. The payload below is the real shape, captured
/// from /predictions?fixture=1575176 on 2026-09-25.
/// </summary>
public class ProviderPredictionTests
{
    private const string Payload = """
        {"get":"predictions","errors":[],"results":1,"response":[{
          "predictions":{
            "winner":{"id":165,"name":"Borussia Dortmund","comment":"Win or draw"},
            "win_or_draw":true,"under_over":"-3.5",
            "goals":{"home":"-3.5","away":"-2.5"},
            "advice":"Double chance : Borussia Dortmund or draw",
            "percent":{"home":"45%","draw":"45%","away":"10%"}},
          "comparison":{
            "form":{"home":"63%","away":"37%"},
            "att":{"home":"53%","away":"47%"},
            "def":{"home":"80%","away":"20%"},
            "poisson_distribution":{"home":"100%","away":"0%"},
            "h2h":{"home":"85%","away":"15%"},
            "goals":{"home":"75%","away":"25%"},
            "total":{"home":"71.2%","away":"28.8%"}},
          "teams":{
            "home":{"name":"Borussia Dortmund","last_5":{"played":4,"form":"100%","att":"64%","def":"86%",
              "goals":{"for":{"total":9,"average":"2.3"},"against":{"total":2,"average":"0.5"}}}},
            "away":{"name":"Werder Bremen","last_5":{"played":4,"form":"58%","att":"57%","def":"43%",
              "goals":{"for":{"total":8,"average":"2.0"},"against":{"total":8,"average":"2.0"}}}}}
        }]}
        """;

    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? RequestedPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestedPath = request.RequestUri?.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static ApiFootballService Service(Handler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://v3.football.api-sports.io") },
            Mock.Of<IApiQuotaTracker>(), Mock.Of<IApiCallTracker>(),
            NullLogger<ApiFootballService>.Instance);

    [Fact]
    public async Task PercentagesBecomeSharesAndTheComparisonIsReadFromTheHomeSide()
    {
        var handler = new Handler(Payload);

        var prediction = await Service(handler).GetPredictionAsync(1575176);

        handler.RequestedPath.Should().Contain("/predictions?fixture=1575176");
        prediction.Should().NotBeNull();
        prediction!.PercentHome.Should().BeApproximately(.45, 1e-9);
        prediction.PercentDraw.Should().BeApproximately(.45, 1e-9);
        prediction.PercentAway.Should().BeApproximately(.10, 1e-9);
        prediction.Form.Should().BeApproximately(.63, 1e-9);
        prediction.Attack.Should().BeApproximately(.53, 1e-9);
        prediction.Defence.Should().BeApproximately(.80, 1e-9);
        prediction.HeadToHead.Should().BeApproximately(.85, 1e-9);
        prediction.Total.Should().BeApproximately(.712, 1e-9, "a fractional percentage must survive");
    }

    [Fact]
    public async Task EachSidesRecentScoringIsCarried()
    {
        var prediction = await Service(new Handler(Payload)).GetPredictionAsync(1575176);

        prediction!.Home!.Played.Should().Be(4);
        prediction.Home.Form.Should().BeApproximately(1.0, 1e-9);
        prediction.Home.GoalsForAverage.Should().BeApproximately(2.3, 1e-9);
        prediction.Home.GoalsAgainstAverage.Should().BeApproximately(0.5, 1e-9);
        prediction.Away!.GoalsAgainstAverage.Should().BeApproximately(2.0, 1e-9);
        prediction.Advice.Should().Be("Double chance : Borussia Dortmund or draw");
        prediction.WinnerName.Should().Be("Borussia Dortmund");
        prediction.UnderOver.Should().Be("-3.5");
    }

    /// <summary>
    /// Lower divisions often have no prediction. That is an absence, not a
    /// failure, and must not stop a sync.
    /// </summary>
    [Fact]
    public async Task AnEmptyResponseIsNullRatherThanAnError()
    {
        var empty = """{"get":"predictions","errors":[],"results":0,"response":[]}""";

        var prediction = await Service(new Handler(empty)).GetPredictionAsync(999);

        prediction.Should().BeNull();
    }

    /// <summary>A field the provider omits stays null instead of becoming zero.</summary>
    [Fact]
    public async Task MissingFieldsDoNotBecomeZero()
    {
        var sparse = """
            {"errors":[],"results":1,"response":[{"predictions":{"percent":{"home":"40%","draw":null,"away":"60%"}},
             "comparison":{},"teams":{}}]}
            """;

        var prediction = await Service(new Handler(sparse)).GetPredictionAsync(1);

        prediction!.PercentHome.Should().BeApproximately(.40, 1e-9);
        prediction.PercentDraw.Should().BeNull();
        prediction.Form.Should().BeNull();
        prediction.Home.Should().BeNull();
        prediction.Advice.Should().BeNull();
    }
}
