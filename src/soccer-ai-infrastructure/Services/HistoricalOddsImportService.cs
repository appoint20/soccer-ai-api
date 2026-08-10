using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services;
using SoccerAi.Application.Services.Odds;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Imports historical Bet365 prices from football-data.co.uk season files.
///
/// Everything here is written to be auditable rather than clever. A price is
/// only ever written when the external row was confidently matched to exactly
/// one fixture, the fixture has no price yet, and the value passes the odds
/// guard. Anything else is counted and reported so the operator can see what
/// the import did and did not do.
/// </summary>
public sealed class HistoricalOddsImportService(
    IApplicationDbContext dbContext,
    ILeagueTierService leagueTiers,
    IHttpClientFactory httpClientFactory,
    IOptions<HistoricalOddsOptions> options,
    ILogger<HistoricalOddsImportService> logger) : IHistoricalOddsImportService
{
    /// <summary>Named client so timeouts and headers stay configurable in one place.</summary>
    public const string HttpClientName = "football-data-csv";

    /// <summary>
    /// Recorded as the quote source so these rows are never mistaken for a live
    /// capture. Line-movement analysis depends on knowing which prices were
    /// observed in real time and which were imported after the fact.
    /// </summary>
    private const string QuoteSource = "Bet365Historical";

    public async Task<HistoricalOddsImportResult> ImportAsync(
        IReadOnlyCollection<int> seasons, bool dryRun, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(seasons);

        var opt = options.Value;
        var results = new List<HistoricalOddsSeasonResult>();

        var leagueIds = leagueTiers.GetSyncLeagueIds()
            .Where(opt.Divisions.ContainsKey)
            .ToList();

        foreach (var leagueId in leagueIds)
        {
            var teams = await LoadLeagueTeamsAsync(leagueId, ct);
            if (teams.Count == 0)
            {
                logger.LogWarning("[OddsImport] League {LeagueId} has no teams — skipping", leagueId);
                continue;
            }

            foreach (var season in seasons.Order())
            {
                ct.ThrowIfCancellationRequested();
                results.Add(await ImportSeasonAsync(leagueId, season, teams, opt, dryRun, ct));
            }
        }

        return new HistoricalOddsImportResult(results);
    }

    private async Task<HistoricalOddsSeasonResult> ImportSeasonAsync(
        int leagueId, int season, IReadOnlyList<TeamCandidate> teams,
        HistoricalOddsOptions opt, bool dryRun, CancellationToken ct)
    {
        var division = opt.Divisions[leagueId];
        var seasonCode = SeasonCode(season);
        var url = $"{opt.BaseUrl.TrimEnd('/')}/{seasonCode}/{division}.csv";

        string csv;
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);

            csv = DecodeCsv(await client.GetByteArrayAsync(url, ct));
        }
        catch (Exception ex)
        {
            // A missing file is normal: not every division exists in every season.
            logger.LogWarning("[OddsImport] {Division} {Season}: {Message}", division, seasonCode, ex.Message);
            return new HistoricalOddsSeasonResult(
                leagueId, division, seasonCode, 0, 0, 0, 0, 0, [], ex.Message);
        }

        var rows = FootballDataCsvParser.Parse(csv);
        if (rows.Count == 0)
            return new HistoricalOddsSeasonResult(leagueId, division, seasonCode, 0, 0, 0, 0, 0, []);

        var fixtures = await LoadSeasonFixturesAsync(leagueId, rows, opt, ct);
        var unmatchedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int matched = 0, priced = 0, alreadyPriced = 0, notFound = 0;

        foreach (var row in rows)
        {
            if (!row.HasAnyPrice) continue;

            var home = TeamNameMatcher.Match(row.HomeTeam, teams, opt.MinTeamNameSimilarity);
            var away = TeamNameMatcher.Match(row.AwayTeam, teams, opt.MinTeamNameSimilarity);

            if (home is null) unmatchedNames.Add(row.HomeTeam);
            if (away is null) unmatchedNames.Add(row.AwayTeam);
            if (home is null || away is null) continue;

            var fixture = FindFixture(fixtures, row, home.ApiId, away.ApiId, opt.DateToleranceDays);
            if (fixture is null) { notFound++; continue; }

            matched++;

            if (FixtureOddsWriter.HasAnyValidPrice(
                    fixture.HomeWinOdds, fixture.DrawOdds, fixture.AwayWinOdds,
                    fixture.Over25Odds, fixture.Under25Odds, fixture.BttsYesOdds))
            {
                // A live capture is closer to what a customer saw; never overwrite it.
                alreadyPriced++;
                continue;
            }

            if (dryRun) { priced++; continue; }

            if (ApplyPrices(fixture, row)) priced++;
        }

        if (!dryRun && priced > 0)
            await dbContext.SaveChangesAsync(ct);

        logger.LogInformation(
            "[OddsImport] {Division} {Season}: {Rows} rows, {Matched} matched, {Priced} priced, "
            + "{Already} already had prices, {NotFound} no fixture, {Unmatched} unknown names",
            division, seasonCode, rows.Count, matched, priced, alreadyPriced, notFound, unmatchedNames.Count);

        return new HistoricalOddsSeasonResult(
            leagueId, division, seasonCode, rows.Count, matched, priced, alreadyPriced, notFound,
            [.. unmatchedNames.Order()]);
    }

    // ── Writing ──────────────────────────────────────────────────────────────

    private bool ApplyPrices(Fixture fixture, FootballDataRow row)
    {
        var wrote = FixtureOddsWriter.ApplyBestPrices(
            fixture,
            new FixtureOdds(
                row.HomeWin, row.Draw, row.AwayWin,
                row.Over25, row.Under25,
                // These files carry no BTTS market.
                BttsYes: null, BttsNo: null));

        if (!wrote) return false;

        // Provenance: recorded under a distinct source name and stamped with the
        // fixture date, so this never masquerades as a live capture in the
        // quote history that line-movement analysis reads.
        var observedAt = fixture.Date.AddDays(-2);

        foreach (var (market, price) in new (string Market, double? Price)[]
                 {
                     (OddsMarkets.HomeWin, row.HomeWin),
                     (OddsMarkets.Draw, row.Draw),
                     (OddsMarkets.AwayWin, row.AwayWin),
                     (OddsMarkets.Over25, row.Over25),
                     (OddsMarkets.Under25, row.Under25)
                 })
        {
            if (price is null) continue;

            dbContext.FixtureOddsQuotes.Add(new FixtureOddsQuote
            {
                FixtureId = fixture.Id,
                Bookmaker = QuoteSource,
                Market = market,
                Price = price.Value,
                CapturedAtUtc = observedAt
            });
        }

        return true;
    }

    // ── Loading and matching ─────────────────────────────────────────────────

    private async Task<IReadOnlyList<TeamCandidate>> LoadLeagueTeamsAsync(int leagueId, CancellationToken ct)
    {
        // Teams that actually played in this league: matching against every team
        // in the database would invite cross-country collisions.
        var pairs = await dbContext.Fixtures
            .AsNoTracking()
            .Where(f => f.LeagueId == leagueId)
            .Select(f => new { f.HomeTeamId, f.AwayTeamId })
            .Distinct()
            .ToListAsync(ct);

        var teamIds = pairs
            .SelectMany(p => new[] { p.HomeTeamId, p.AwayTeamId })
            .Distinct()
            .ToList();

        return await dbContext.Teams
            .AsNoTracking()
            .Where(t => teamIds.Contains(t.ApiId))
            .Select(t => new TeamCandidate(t.ApiId, t.Name, t.ShortName))
            .ToListAsync(ct);
    }

    private async Task<List<Fixture>> LoadSeasonFixturesAsync(
        int leagueId, IReadOnlyList<FootballDataRow> rows, HistoricalOddsOptions opt, CancellationToken ct)
    {
        var from = rows.Min(r => r.Date).AddDays(-opt.DateToleranceDays);
        var to = rows.Max(r => r.Date).AddDays(opt.DateToleranceDays + 1);

        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        return await dbContext.Fixtures
            .Where(f => f.LeagueId == leagueId && f.Date >= fromUtc && f.Date < toUtc)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Both teams must agree, and the date must be within tolerance. Ambiguity
    /// returns nothing: the same pairing twice in three days is rare, but
    /// picking the wrong one would attach a price to the wrong result.
    /// </summary>
    private static Fixture? FindFixture(
        IReadOnlyList<Fixture> fixtures, FootballDataRow row, int homeApiId, int awayApiId, int toleranceDays)
    {
        var candidates = fixtures
            .Where(f => f.HomeTeamId == homeApiId && f.AwayTeamId == awayApiId)
            .Where(f => Math.Abs(DateOnly.FromDateTime(f.Date.UtcDateTime).DayNumber - row.Date.DayNumber)
                        <= toleranceDays)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>
    /// These season files are not consistently encoded: most are Windows-1252
    /// ("King's Lynn" with a curly quote), but some are UTF-8 ("Preußen
    /// Münster"). Assuming either one corrupts the other's names, and a
    /// corrupted name silently fails to match — which is how "PreuÃŸen
    /// MÃ¼nster" reached an import report.
    ///
    /// Strict UTF-8 decoding settles it: byte sequences that are valid UTF-8
    /// are essentially never Windows-1252 text by accident, so a decode failure
    /// is a reliable signal rather than a guess.
    /// </summary>
    public static string DecodeCsv(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Latin-1 never fails and preserves every byte, leaving the name
            // normalizer to strip whatever it does not recognise.
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Season 2025 (i.e. 2025/26) is published as "2526".</summary>
    public static string SeasonCode(int startYear) =>
        string.Create(CultureInfo.InvariantCulture, $"{startYear % 100:D2}{(startYear + 1) % 100:D2}");
}
