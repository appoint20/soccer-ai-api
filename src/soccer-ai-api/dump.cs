using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mediator.Net;
using System.Text.Json;
using SoccerAi.Application.Features.Analysis;

public class Dumper {
    public static async Task Dump(IServiceProvider sp) {
        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var response = await mediator.RequestAsync<GetMatchAnalysisQuery, GetMatchAnalysisResponse>(new GetMatchAnalysisQuery { Date = new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero) });
        var json = JsonSerializer.Serialize(response.Matches.Take(1).ToList(), new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(json);
    }
}
