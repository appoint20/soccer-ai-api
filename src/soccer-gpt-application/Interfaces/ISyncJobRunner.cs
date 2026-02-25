using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface ISyncJobRunner
{
    Task<SyncResult> RunStandingsAsync(int season, CancellationToken cancellationToken);
    Task<SyncResult> RunFixturesAsync(int season, CancellationToken cancellationToken);
    Task<int> RunGeminiAsync(CancellationToken cancellationToken);
    Task<bool> RunMlTrainingAsync(CancellationToken cancellationToken);
    Task<SyncResult> RunNightlyAsync(int season, CancellationToken cancellationToken);
}
