namespace soccer_gpt_application.Interfaces;

public interface IScheduledJobService
{
    Task ExecuteDailyJobAsync(CancellationToken cancellationToken);
}
