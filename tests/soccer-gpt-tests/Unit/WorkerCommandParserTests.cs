using soccer_gpt_worker.Worker;

namespace soccer_gpt_tests.Unit;

public class WorkerCommandParserTests
{
    [Fact]
    public void TryParse_NoArgs_DefaultsToNightly()
    {
        var ok = WorkerCommandParser.TryParse([], out var command, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(WorkerJob.Nightly, command.Job);
        Assert.Null(command.Season);
        Assert.False(command.IsHelp);
    }

    [Fact]
    public void TryParse_WithJobAndSeason_ParsesValues()
    {
        var ok = WorkerCommandParser.TryParse(["--job", "fixtures", "--season", "2025"], out var command, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(WorkerJob.Fixtures, command.Job);
        Assert.Equal(2025, command.Season);
    }

    [Fact]
    public void TryParse_InvalidSeason_ReturnsError()
    {
        var ok = WorkerCommandParser.TryParse(["--job", "fixtures", "--season", "abc"], out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("Invalid season", error);
    }

    [Fact]
    public void TryParse_UnknownJob_ReturnsError()
    {
        var ok = WorkerCommandParser.TryParse(["--job", "unknown"], out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("Unknown job", error);
    }
}
