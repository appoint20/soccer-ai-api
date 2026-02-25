namespace soccer_gpt_worker.Worker;

public static class WorkerCommandParser
{
    public static bool TryParse(string[] args, out WorkerCommand command, out string? error)
    {
        command = new WorkerCommand(WorkerJob.Nightly, null);
        error = null;

        if (args.Length == 0)
            return true;

        string? jobValue = null;
        int? season = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim();

            if (arg is "--help" or "-h")
            {
                command = new WorkerCommand(WorkerJob.Nightly, null, IsHelp: true);
                return true;
            }

            if (arg is "--job" or "-j")
            {
                if (i + 1 >= args.Length)
                {
                    error = "Missing value for --job.";
                    return false;
                }

                jobValue = args[++i].Trim();
                continue;
            }

            if (arg is "--season" or "-s")
            {
                if (i + 1 >= args.Length)
                {
                    error = "Missing value for --season.";
                    return false;
                }

                var rawSeason = args[++i].Trim();
                if (!int.TryParse(rawSeason, out var parsedSeason))
                {
                    error = $"Invalid season '{rawSeason}'. Expected numeric year (example: 2025).";
                    return false;
                }

                season = parsedSeason;
                continue;
            }

            error = $"Unknown argument '{arg}'.";
            return false;
        }

        if (jobValue == null)
        {
            command = new WorkerCommand(WorkerJob.Nightly, season);
            return true;
        }

        if (!TryMapJob(jobValue, out var job))
        {
            error = $"Unknown job '{jobValue}'. Allowed jobs: nightly, standings, fixtures, gemini, ml.";
            return false;
        }

        command = new WorkerCommand(job, season);
        return true;
    }

    public static string HelpText => """
Usage:
  dotnet soccer-gpt-worker.dll --job <nightly|standings|fixtures|gemini|ml> [--season <year>]

Examples:
  dotnet soccer-gpt-worker.dll --job standings --season 2025
  dotnet soccer-gpt-worker.dll --job fixtures --season 2025
  dotnet soccer-gpt-worker.dll --job gemini
  dotnet soccer-gpt-worker.dll --job ml
  dotnet soccer-gpt-worker.dll --job nightly --season 2025
""";

    private static bool TryMapJob(string raw, out WorkerJob job)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "nightly":
                job = WorkerJob.Nightly;
                return true;
            case "standings":
                job = WorkerJob.Standings;
                return true;
            case "fixtures":
                job = WorkerJob.Fixtures;
                return true;
            case "gemini":
                job = WorkerJob.Gemini;
                return true;
            case "ml":
                job = WorkerJob.Ml;
                return true;
            default:
                job = default;
                return false;
        }
    }
}
