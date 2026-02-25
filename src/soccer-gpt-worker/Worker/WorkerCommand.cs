namespace soccer_gpt_worker.Worker;

public sealed record WorkerCommand(WorkerJob Job, int? Season, bool IsHelp = false);
