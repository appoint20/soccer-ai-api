using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models.Deterministic;

namespace SoccerAi.Infrastructure.Services;

public class NlpService(HttpClient httpClient, ILogger<NlpService> logger) : INlpService
{
    public async Task<NlpIntent> ParseIntentAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new NlpIntent
            {
                NumMatches = new List<int> { 2, 3 },
                BetType = "win",
                MinOdds = 1.0,
                Filters = new NlpFilters { MinProbability = 0.6 }
            };
        }

        try
        {
            var response = await httpClient.PostAsJsonAsync("nlp/parse", new { query });
            response.EnsureSuccessStatusCode();
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = await response.Content.ReadFromJsonAsync<NlpIntent>(options);
            
            return result ?? new NlpIntent { NumMatches = new List<int> { 2, 3 }, BetType = "win" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse NLP intent for query: {Query}", query);
            return new NlpIntent(); // Fail gracefully with default empty intent
        }
    }
}
