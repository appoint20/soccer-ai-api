using Mediator.Net.Contracts;

namespace soccer_gpt_application.Features.DataManagement.Commands;

public class UploadUpcomingFixturesCommand : ICommand
{
    public Stream FileStream { get; init; } = Stream.Null;
    public string FileName { get; init; } = string.Empty;
}

public class UploadUpcomingFixturesResponse : IResponse
{
    public int ProcessedCount { get; init; }
    public int ValidCount { get; init; }
    public int AddedCount { get; init; }
    public int SkippedDuplicate { get; init; }
    public int SkippedInvalid { get; init; }
    public List<string> Errors { get; init; } = [];
}
