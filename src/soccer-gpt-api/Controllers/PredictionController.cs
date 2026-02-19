using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_api.Controllers;

/// <summary>
/// API controller for ML predictions.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PredictionController(IMlPredictionService predictionService) : ControllerBase
{
    /// <summary>
    /// Get ML predictions for a fixture.
    /// </summary>
    [HttpGet("{fixtureId:int}")]
    public async Task<IActionResult> GetPrediction(int fixtureId, CancellationToken ct)
    {
        var prediction = await predictionService.PredictAsync(fixtureId, ct);
        
        if (prediction == null)
            return NotFound(new { error = "Prediction not available or fixture not found" });
        
        return Ok(prediction);
    }
    
    /// <summary>
    /// Get predictions from raw features (for testing).
    /// </summary>
    [HttpPost("features")]
    public async Task<IActionResult> PredictFromFeatures([FromBody] float[] features, CancellationToken ct)
    {
        if (features.Length != 30) // 25 base + 5 odds-implied
            return BadRequest(new { error = $"Expected 30 features, got {features.Length}" });
        
        var predictions = await predictionService.PredictFromFeaturesAsync(features, ct);
        
        return Ok(predictions);
    }
}
