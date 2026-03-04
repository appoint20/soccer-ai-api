using Microsoft.ML.Data;

namespace SoccerAi.Infrastructure.MlNet.Models;

public class BinaryPrediction
{
    [ColumnName("PredictedLabel")]
    public bool Prediction { get; set; }
    
    public float Probability { get; set; }
    public float Score { get; set; }
}

public class MulticlassPrediction
{
    [ColumnName("PredictedLabel")]
    public string Prediction { get; set; } = "";
    
    public float[] Score { get; set; } = Array.Empty<float>();
}
