using Mediator.Net.Contracts;

namespace soccer_gpt_application.Features.HistoricalMatches.Commands;

public class UploadHistoricalDataCommand : ICommand
{
    public Stream FileStream { get; init; } = Stream.Null;
    public string FileName { get; init; } = string.Empty;
}

public class UploadHistoricalDataResponse : IResponse
{
    public int ProcessedCount { get; set; }
    public int ValidCount { get; init; }
    public int AddedCount { get; set; }
    public int SkippedDuplicate { get; set; }
    public int SkippedInvalid { get; init; }
    public List<string> Errors { get; init; } = [];
}
