using System.Text;
using FluentAssertions;
using SoccerAi.Infrastructure.Services;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// The season files are not consistently encoded. Assuming one encoding
/// corrupts the other's team names, and a corrupted name fails to match without
/// any error being raised — the fixture is simply skipped.
/// </summary>
public class CsvDecodingTests
{
    [Fact]
    public void ReadsWindows1252Files()
    {
        // "King's Lynn" with the Windows-1252 curly apostrophe (0x92), which is
        // not valid UTF-8.
        byte[] bytes = [.. "King"u8, 0x92, .. "s Lynn"u8];

        var decoded = HistoricalOddsImportService.DecodeCsv(bytes);

        decoded.Should().StartWith("King").And.EndWith("s Lynn");
        decoded.Should().NotContain("�", "a replacement character means the name was destroyed");
    }

    [Fact]
    public void ReadsUtf8Files()
    {
        // Some files really are UTF-8. Forcing Latin-1 turns this into
        // "PreuÃŸen MÃ¼nster", which is how it reached an import report.
        var bytes = Encoding.UTF8.GetBytes("Preußen Münster");

        HistoricalOddsImportService.DecodeCsv(bytes).Should().Be("Preußen Münster");
    }

    [Fact]
    public void PlainAsciiIsUnaffected() =>
        HistoricalOddsImportService.DecodeCsv("Liverpool,Bournemouth"u8.ToArray())
            .Should().Be("Liverpool,Bournemouth");

    [Fact]
    public void EmptyContentDoesNotThrow() =>
        HistoricalOddsImportService.DecodeCsv([]).Should().BeEmpty();
}
