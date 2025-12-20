
using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public class ApiPredictionResponse
{
    [JsonPropertyName("response")]
    public List<ApiFootballPrediction> Response { get; set; } = [];
}
