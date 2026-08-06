using FluentAssertions;
using SoccerAi.Application.Services.Odds;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// Parsing these files is where a locale bug once turned 1.85 into 185 and
/// corrupted every expected-value calculation downstream. These tests hold that
/// door shut.
/// </summary>
public class FootballDataCsvParserTests
{
    private const string Header =
        "Div,Date,Time,HomeTeam,AwayTeam,FTHG,FTAG,FTR,B365H,B365D,B365A,B365>2.5,B365<2.5,B365CH,B365CD,B365CA";

    [Fact]
    public void ParsesARealRow()
    {
        var csv = Header + "\n" +
                  "E0,15/08/2025,20:00,Liverpool,Bournemouth,4,2,H,1.3,6,8.5,1.36,3.2,1.29,6.25,9\n";

        var row = FootballDataCsvParser.Parse(csv).Should().ContainSingle().Subject;

        row.Date.Should().Be(new DateOnly(2025, 8, 15));
        row.HomeTeam.Should().Be("Liverpool");
        row.AwayTeam.Should().Be("Bournemouth");
        row.HomeGoals.Should().Be(4);
        row.AwayGoals.Should().Be(2);
        row.HomeWin.Should().Be(1.3);
        row.Draw.Should().Be(6);
        row.AwayWin.Should().Be(8.5);
        row.Over25.Should().Be(1.36);
        row.Under25.Should().Be(3.2);
    }

    [Fact]
    public void ResolvesColumnsByNameNotPosition()
    {
        // Bookmakers come and go between seasons, so the column order shifts.
        var csv = "Date,HomeTeam,AwayTeam,B365D,B365A,B365H\n" +
                  "15/08/2025,Liverpool,Bournemouth,6,8.5,1.3\n";

        var row = FootballDataCsvParser.Parse(csv).Single();

        row.HomeWin.Should().Be(1.3);
        row.AwayWin.Should().Be(8.5);
    }

    [Fact]
    public void FallsBackToClosingOddsWhenPreClosingIsAbsent()
    {
        var csv = Header + "\n" +
                  "E0,15/08/2025,20:00,Liverpool,Bournemouth,4,2,H,,,,,,1.29,6.25,9\n";

        var row = FootballDataCsvParser.Parse(csv).Single();

        row.HomeWin.Should().Be(1.29, "the closing price is better than no price");
    }

    [Fact]
    public void PricesAreReadAsEnglishDecimalsWhateverTheMachineLocale()
    {
        // Under a German locale "1.85" parses as 185 — the bug that once
        // corrupted this database and produced ROI figures of 4900%.
        FootballDataCsvParser.ParsePrice("1.85").Should().Be(1.85);
    }

    [Fact]
    public void ImplausiblePricesAreRejectedNotRescaled()
    {
        FootballDataCsvParser.ParsePrice("185").Should().BeNull();
        FootballDataCsvParser.ParsePrice("0.5").Should().BeNull();
    }

    [Fact]
    public void MissingPriceIsNull()
    {
        FootballDataCsvParser.ParsePrice("").Should().BeNull();
        FootballDataCsvParser.ParsePrice(null).Should().BeNull();
    }

    [Theory]
    [InlineData("15/08/2025", 2025, 8, 15)]
    [InlineData("15/08/25", 2025, 8, 15)]
    [InlineData("5/8/2025", 2025, 8, 5)]
    public void ReadsBothDateFormats(string raw, int year, int month, int day) =>
        FootballDataCsvParser.ParseDate(raw).Should().Be(new DateOnly(year, month, day));

    [Fact]
    public void RejectsAmbiguousAmericanDates()
    {
        // These files are dd/mm. Reading 08/15/2025 as a date would silently
        // shift every fixture by months.
        FootballDataCsvParser.ParseDate("08/15/2025").Should().BeNull();
    }

    [Fact]
    public void SkipsPaddingRows()
    {
        var csv = Header + "\n" +
                  "E0,15/08/2025,20:00,Liverpool,Bournemouth,4,2,H,1.3,6,8.5,1.36,3.2,1.29,6.25,9\n" +
                  ",,,,,,,,,,,,,,,\n" +
                  "\n";

        FootballDataCsvParser.Parse(csv).Should().ContainSingle();
    }

    [Fact]
    public void HandlesShortRowsWithoutThrowing()
    {
        var csv = Header + "\n" + "E0,15/08/2025,20:00,Liverpool,Bournemouth\n";

        var row = FootballDataCsvParser.Parse(csv).Should().ContainSingle().Subject;

        row.HomeWin.Should().BeNull();
        row.HasAnyPrice.Should().BeFalse();
    }

    [Fact]
    public void EmptyOrHeaderOnlyFileYieldsNothing()
    {
        FootballDataCsvParser.Parse("").Should().BeEmpty();
        FootballDataCsvParser.Parse(Header).Should().BeEmpty();
    }

    [Fact]
    public void HandlesQuotedTeamNamesContainingCommas()
    {
        var csv = "Date,HomeTeam,AwayTeam,B365H\n" +
                  "15/08/2025,\"Munich, 1860\",Bayern,2.5\n";

        var row = FootballDataCsvParser.Parse(csv).Single();

        row.HomeTeam.Should().Be("Munich, 1860");
        row.HomeWin.Should().Be(2.5);
    }
}
