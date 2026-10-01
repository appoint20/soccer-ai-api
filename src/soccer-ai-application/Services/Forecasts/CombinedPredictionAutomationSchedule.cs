using System.Globalization;
using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services.Forecasts;

public static class CombinedPredictionAutomationSchedule
{
    public static string? WindowFor(DateTimeOffset kickoff, DateTimeOffset now, DateTimeOffset? latestCapture,
        CombinedPredictionAutomationOptions options)
    {
        if (kickoff <= now.AddMinutes(options.MinimumLeadMinutes) || kickoff > now.AddHours(options.HorizonHours))
            return null;
        var finalStart = kickoff.AddHours(-options.FinalWindowHours);
        if (now >= finalStart)
            return latestCapture >= finalStart ? null : "final";
        var day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        return latestCapture >= day ? null : "daily:" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
