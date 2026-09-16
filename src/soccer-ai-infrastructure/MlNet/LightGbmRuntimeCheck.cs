using System.Runtime.InteropServices;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.LightGbm;

namespace SoccerAi.Infrastructure.MlNet;

/// <summary>A synthetic native-training smoke check, safe before host/DB construction.</summary>
public static class LightGbmRuntimeCheck
{
    public static int Run()
    {
        try
        {
            var ml = new MLContext(seed: 42);
            var data = ml.Data.LoadFromEnumerable(Enumerable.Range(0, 64)
                .Select(i => new SmokeRow { Feature = i % 8, Label = 1 + i % 3 }));
            var estimator = ml.Transforms.Concatenate("Features", nameof(SmokeRow.Feature))
                .Append(ml.Regression.Trainers.LightGbm(new LightGbmRegressionTrainer.Options
                {
                    LabelColumnName = nameof(SmokeRow.Label), FeatureColumnName = "Features",
                    NumberOfIterations = 4, NumberOfLeaves = 4,
                    MinimumExampleCountPerLeaf = 2, NumberOfThreads = 1
                }));
            var model = estimator.Fit(data);
            if (model.Transform(data).GetColumn<float>("Score").Any(p => !float.IsFinite(p)))
                throw new InvalidDataException("Native trainer produced non-finite test predictions.");
            Console.WriteLine($"LightGBM native fit/score OK ({RuntimeInformation.RuntimeIdentifier}, {RuntimeInformation.ProcessArchitecture}).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"LightGBM native check FAILED ({RuntimeInformation.RuntimeIdentifier}, {RuntimeInformation.ProcessArchitecture}). "
                + "On the Linux deployment image, install libgomp1 and use the packaged linux-x64 native library. "
                + "Inspect lib_lightgbm.so with ldd for missing dependencies.");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private sealed class SmokeRow
    {
        public float Feature { get; set; }
        public float Label { get; set; }
    }
}
