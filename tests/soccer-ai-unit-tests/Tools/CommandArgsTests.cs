using FluentAssertions;
using SoccerAi.Tools;

namespace soccer_ai_unit_tests.Tools;

/// <summary>
/// Argument parsing that silently ignores an option is worse than one that
/// fails: the command runs against its default and reports a problem that has
/// nothing to do with the real mistake.
/// </summary>
public class CommandArgsTests
{
    [Fact]
    public void ReadsAValue() =>
        CommandArgs.String(["migrate-data", "--sqlite=data/soccer.db"], "--sqlite")
            .Should().Be("data/soccer.db");

    [Fact]
    public void SurvivesLineContinuationsTypedOnOneLine()
    {
        // A backslash typed without a real newline reaches the process as part
        // of the next argument: " --sqlite=path". Matched strictly, the option
        // disappears and the command silently uses its default — which then
        // reports a missing file the user never asked for.
        string[] args = ["migrate-data", "\\ --sqlite=data/soccer.db", "\\ --postgres=postgres://x"];

        CommandArgs.String(args, "--sqlite").Should().Be("data/soccer.db");
        CommandArgs.String(args, "--postgres").Should().Be("postgres://x");
    }

    [Fact]
    public void StripsSurroundingQuotes() =>
        CommandArgs.String(["--postgres=\"postgres://user:pw@host/db\""], "--postgres")
            .Should().Be("postgres://user:pw@host/db");

    [Fact]
    public void MissingOptionIsNull() =>
        CommandArgs.String(["migrate-data"], "--sqlite").Should().BeNull();

    [Fact]
    public void ValuesContainingEqualsSignsSurvive() =>
        CommandArgs.String(["--postgres=Host=db;Password=a=b"], "--postgres")
            .Should().Be("Host=db;Password=a=b");

    [Fact]
    public void ParsesNumbersInvariantly()
    {
        // A German locale must not read "1.5" as 15.
        CommandArgs.Double(["--stake=1.5"], "--stake").Should().Be(1.5);
        CommandArgs.Int(["--weeks=30"], "--weeks").Should().Be(30);
    }

    [Fact]
    public void FlagsAreDetectedWithOrWithoutStrayBackslashes()
    {
        CommandArgs.Flag(["--dry-run"], "--dry-run").Should().BeTrue();
        CommandArgs.Flag(["\\ --dry-run"], "--dry-run").Should().BeTrue();
        CommandArgs.Flag(["--probe"], "--dry-run").Should().BeFalse();
    }
}

/// <summary>
/// Console output reaches screenshots and chat messages. A managed database URL
/// carries its password inline, so printing one unredacted leaks it.
/// </summary>
public class ConnectionStringRedactorTests
{
    [Fact]
    public void RedactsThePasswordInAUrl() =>
        ConnectionStringRedactor.Redact("postgresql://soccer:s3cr3tValue@dpg-abc.render.com/soccer_x")
            .Should().Be("postgresql://soccer:****@dpg-abc.render.com/soccer_x");

    [Fact]
    public void RedactsAKeyValuePassword() =>
        ConnectionStringRedactor.Redact("Host=db;Database=soccer;Username=postgres;Password=s3cr3t")
            .Should().Be("Host=db;Database=soccer;Username=postgres;Password=****");

    [Fact]
    public void KeepsEverythingUsefulForDiagnosis()
    {
        var redacted = ConnectionStringRedactor.Redact(
            "postgresql://soccer:s3cr3t@dpg-abc.frankfurt-postgres.render.com/soccer_x");

        redacted.Should().Contain("dpg-abc.frankfurt-postgres.render.com");
        redacted.Should().Contain("soccer_x");
        redacted.Should().NotContain("s3cr3t");
    }

    [Fact]
    public void HandlesNothingToRedact()
    {
        ConnectionStringRedactor.Redact("Data Source=data/soccer.db")
            .Should().Be("Data Source=data/soccer.db");

        ConnectionStringRedactor.Redact(null).Should().BeEmpty();
    }
}
