using Microsoft.ML.Data;

namespace SoccerAi.Infrastructure.MlNet.Models;

public class MatchTrainingData
{
    [VectorType(62)]
    public float[] Features { get; set; } = Array.Empty<float>();

    public bool TargetBtts { get; set; }
    public bool TargetOver25 { get; set; }
    public bool TargetGoals23 { get; set; }
    
    [ColumnName("TargetResult")]
    public string TargetResult { get; set; } = "Home"; // "Home", "Draw", "Away"
}
