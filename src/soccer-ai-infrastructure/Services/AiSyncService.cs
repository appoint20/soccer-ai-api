using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

using SoccerAi.Application.Exceptions;
using SoccerAi.Application.Services.Analysis;

namespace SoccerAi.Infrastructure.Services;

public class AiSyncService(
    IApplicationDbContext dbContext,
    IMatchAnalysisService analysisService,
    IAiAnalysisService aiService,
    IAnalysisPrecomputeService precomputeService,
    ILeagueTierService leagueTiers,
    ILogger<AiSyncService> logger)
    : IAiSyncService
{
    /// <summary>
    /// Days of upcoming fixtures a run covers. The product sells analysis of the
    /// coming board, so the default is a horizon rather than a batch size.
    /// </summary>
    public const int DefaultDaysAhead = 5;

    public async Task SyncUpcomingFixturesAsync(
        DateTime now, bool force = false, int daysAhead = DefaultDaysAhead, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[AiSync] Starting batch sync. Current time: {Now}", now);

        // Upcoming means upcoming. The window used to open three days in the
        // PAST while the query ordered ascending, so a run spent its budget
        // narrating fixtures that had already been played before it ever
        // reached tomorrow's — which is why coverage stopped at today.
        var startUtc = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        var endUtc = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero)
            .AddDays(Math.Max(1, daysAhead));

        logger.LogInformation("[AiSync] Window: {Start:u} to {End:u} ({Days} days ahead)",
            startUtc, endUtc, daysAhead);

        var report = await RunWindowAsync(startUtc, endUtc, force, cancellationToken);

        // The pipeline turns this into LastError. A run that left fixtures
        // without text must not be recorded as a clean success.
        if (report.Failed > 0)
            throw new ExternalApiException("AI narratives", report.Attempted == 0
                ? $"AI preparation failed for {report.Failed} fixtures; no narratives generated."
                : $"AI analysis incomplete: {report.Generated} persisted, {report.Failed} failed. Missing fixtures will retry next sync.");
    }

    public async Task<AiSyncReport> SyncDateAsync(
        DateOnly dateUtc, bool force = false, CancellationToken cancellationToken = default)
    {
        // The calendar day GET /api/analyze returns (FixtureQueryHelper): UTC
        // midnight to midnight, so "this date" means the matches the app lists.
        var dayStart = new DateTimeOffset(dateUtc.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        // A narrative is a pre-match forecast. For today the window opens now:
        // a fixture that has kicked off is counted, never narrated.
        var now = DateTimeOffset.UtcNow;
        var startUtc = now > dayStart ? now : dayStart;

        var onDate = await dbContext.Fixtures.AsNoTracking()
            .Where(f => f.Date >= dayStart && f.Date < dayEnd)
            .Select(f => new { f.LeagueId, f.Status, f.Date })
            .ToListAsync(cancellationToken);

        var scope = leagueTiers.GetSyncLeagueIds().ToHashSet();
        var outOfScope = onDate.Count(f => !scope.Contains(f.LeagueId));
        var notUpcoming = onDate.Count(f => scope.Contains(f.LeagueId)
                                            && (f.Date < startUtc || f.Status is not ("NS" or "TBD")));

        logger.LogInformation(
            "[AiSync] Date run for {Date:yyyy-MM-dd} (force {Force}): {Total} fixtures, {OutOfScope} outside the synced leagues, {NotUpcoming} started or not scheduled",
            dateUtc, force, onDate.Count, outOfScope, notUpcoming);

        var report = await RunWindowAsync(startUtc, dayEnd, force, cancellationToken);
        return report with { FixturesOnDate = onDate.Count, OutOfScope = outOfScope, NotUpcoming = notUpcoming };
    }

    /// <summary>
    /// Narrates every not-yet-started, in-scope fixture kicking off in
    /// [<paramref name="startUtc"/>, <paramref name="endUtc"/>).
    /// </summary>
    /// <remarks>
    /// Per-fixture failures are counted and returned rather than thrown, so each
    /// caller decides what an incomplete run means: the scheduled sync fails its
    /// step, a manual date run reports which fixtures to retry. Account and
    /// configuration failures (401, 402, 403, 429, 503) still throw at once.
    /// </remarks>
    private async Task<AiSyncReport> RunWindowAsync(
        DateTimeOffset startUtc, DateTimeOffset endUtc, bool force, CancellationToken cancellationToken)
    {
        // 1. Fetch raw fixtures from DB (focus leagues only). Not-yet-started
        //    only: a finished match already has a result, so paying an LLM to
        //    predict one is pure waste.
        var scopedLeagueIds = leagueTiers.GetSyncLeagueIds().ToList();
        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= startUtc && f.Date < endUtc
                        && (f.Status == "NS" || f.Status == "TBD")
                        && scopedLeagueIds.Contains(f.LeagueId))
            .OrderBy(f => f.Date)
            .ThenBy(f => f.Id)
            .ToListAsync(cancellationToken);

        if (fixtures.Count == 0)
        {
            logger.LogWarning(
                "[AiSync] Found 0 upcoming fixtures between {Start:u} and {End:u}. Check the fixture sync.",
                startUtc, endUtc);
            return new AiSyncReport { WindowStartUtc = startUtc, WindowEndUtc = endUtc };
        }

        logger.LogInformation("[AiSync] Found {Count} total fixtures in window.", fixtures.Count);

        var totalProcessed = 0;
        var toAnalyze = new List<AiBatchItem>();
        var rawByFixture = new Dictionary<int, WeightedPrediction>();
        var skippedCount = 0;
        var explanationAttempts = 0;
        var failedIds = new List<int>();
        var opinionPrompt = aiService.OpinionPromptHash;

        // 2. Filter BEFORE heavy ML prediction
        foreach (var fixture in fixtures)
        {
            var alreadyAnalyzedCount = await dbContext.FixtureAnalyses
                .CountAsync(a => a.FixtureId == fixture.Id && (a.Lang == "en" || a.Lang == "de") &&
                    a.Analysis != null && a.Analysis.Trim() != "" &&
                    (opinionPrompt == null || a.AiPromptHash == opinionPrompt), cancellationToken);

            if (!force && alreadyAnalyzedCount >= 2)
            {
                try
                {
                    if (aiService.SupportsDecisionExplanations && await RefreshExplanationAsync(fixture.Id, false, cancellationToken))
                    {
                        explanationAttempts++;
                        totalProcessed++;
                    }
                    else skippedCount++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (ExternalApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized
                    or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.PaymentRequired
                    or System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable) { throw; }
                catch (Exception ex)
                {
                    explanationAttempts++;
                    failedIds.Add(fixture.Id);
                    logger.LogError(ex, "[AiSync] Final-decision explanation failed for fixture {Id}", fixture.Id);
                }
                continue;
            }

            logger.LogWarning("[AiSync] NOT skipping Fixture {FixtureId} (DB PK={DbId}, ApiId={ApiId}): only {Count}/2 analyses found. Date={Date}, Status={Status}",
                fixture.Id, fixture.Id, fixture.ApiId, alreadyAnalyzedCount, fixture.Date, fixture.Status);

            try
            {
                // refresh: true is load-bearing. With a warm math cache the
                // analysis short-circuits the models and returns
                // PoissonModel.Empty — every Model* field below would be 0.0,
                // and the LLM would be asked to judge a match whose
                // "mathematical probabilities" are all zero.
                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, "en", refresh: true, cancellationToken);

                // The calibrated prediction is what the product acts on, so it
                // is what the model reasons about. Poisson output is the raw
                // pre-calibration layer and belongs in the math cache only.
                var probs = analysis.Prediction ?? new WeightedPrediction();

                toAnalyze.Add(new AiBatchItem
                {
                    FixtureId = analysis.FixtureId,
                    League = analysis.LeagueName,
                    HomeTeam = analysis.TeamStats.Home.Name,
                    AwayTeam = analysis.TeamStats.Away.Name,
                    HomeStats = analysis.TeamStats.Home,
                    AwayStats = analysis.TeamStats.Away,
                    HomeGoalAvg = analysis.TeamStats.Home.AvgGoalsScoredLast7,
                    AwayGoalAvg = analysis.TeamStats.Away.AvgGoalsScoredLast7,
                    ModelHomeWin = probs.HomeProb,
                    ModelDraw = probs.DrawProb,
                    ModelAwayWin = probs.AwayProb,
                    ModelOver25 = probs.Over25Prob,
                    ModelBTTS = probs.BTTSProb,
                    ModelGoals23 = probs.TwoToThreeGoalsProb,
                    ModelBttsAndOver25 = analysis.Models.Poisson.IsValid ? analysis.Models.Poisson.BttsAndOver25 : null,
                    OddsHomeWin = analysis.OddsHomeWin,
                    OddsDraw = analysis.OddsDraw,
                    OddsAwayWin = analysis.OddsAwayWin,
                    OddsOver25 = analysis.OddsOver25,
                    OddsBTTS = analysis.OddsBttsYes,
                });

                rawByFixture[analysis.FixtureId] = analysis.RawPrediction ?? probs;

                if (probs.HomeProb <= 0 && probs.Over25Prob <= 0)
                {
                    logger.LogWarning(
                        "[AiSync] Fixture {FixtureId} produced no model probabilities — the narrative would be "
                        + "written from stats alone. Skipping.", fixture.Id);
                    failedIds.Add(fixture.Id);
                    toAnalyze.RemoveAt(toAnalyze.Count - 1);
                    continue;
                }

                if (analysis.OddsHomeWin == 0 && analysis.OddsAwayWin == 0)
                {
                    logger.LogWarning("[AiSync] Fixture {FixtureId} has ZERO odds. AI analysis might be less accurate.", fixture.Id);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                failedIds.Add(fixture.Id);
                logger.LogError(ex, "[AiSync] Failed to run ML prediction for fixture {FixtureId}.", fixture.Id);
            }
        }

        logger.LogInformation("[AiSync] Skip Summary: {Skipped} already analyzed. {Remaining} matches to process.", skippedCount, toAnalyze.Count);

        if (toAnalyze.Count == 0)
        {
            if (failedIds.Count == 0)
                logger.LogInformation("[AiSync] No matches remaining after filter. Sync complete.");
            return Report();
        }

        logger.LogInformation("[AiSync] Prepared {Count} matches for AI. Processing one fixture per request...", toAnalyze.Count);

        // 3. Process incrementally in batches of 1 to prevent LLM context bleeding/hallucinations.
        for (var i = 0; i < toAnalyze.Count; i += 1)
        {
            var chunkList = toAnalyze.Skip(i).Take(1).ToList();
            try
            {
                await RequireUpcomingFixtureAsync(chunkList[0].FixtureId, cancellationToken);
                logger.LogInformation("[AiSync] Attempting batch {Num} ({Count} matches)...", i + 1, chunkList.Count);
                
                var results = await aiService.AnalyzeBatchAsync(chunkList, cancellationToken);
                if (results.Count != 1 || !results.TryGetValue(chunkList[0].FixtureId, out var candidate) ||
                    AiNarrativeIntegrity.InvalidResult(candidate) is not null)
                    throw new ExternalApiException("AI narratives", "AI returned no complete bilingual analysis for the requested fixture.");
                // A long provider call can cross kickoff. Do not save it as a
                // pre-match assessment merely because preparation started early.
                await RequireUpcomingFixtureAsync(chunkList[0].FixtureId, cancellationToken);
                
                foreach (var (fixtureId, bilingualResult) in results)
                {
                    logger.LogInformation("[AiSync] Ingesting result for Fixture {Id}: {Rec}", fixtureId, bilingualResult.Recommendation);
                    
                    // Find the original analysis to get the math probs
                    var originalAnalysis = chunkList.FirstOrDefault(x => x.FixtureId == fixtureId);
                    if (originalAnalysis == null)
                    {
                        logger.LogWarning("[AiSync] CRITICAL: AI returned FixtureId {Id} which was NOT in the source batch! Skipping.", fixtureId);
                        continue;
                    }

                    // The math cache is the isotonic layer's training data and
                    // must hold RAW probabilities — the same convention
                    // AnalysisPrecomputeService writes. Feeding calibrated
                    // values back in would make the calibration self-correct.
                    var mathProbs = rawByFixture.GetValueOrDefault(fixtureId) ?? new WeightedPrediction
                    {
                        HomeProb = originalAnalysis.ModelHomeWin,
                        Over25Prob = originalAnalysis.ModelOver25,
                        BTTSProb = originalAnalysis.ModelBTTS,
                        DrawProb = originalAnalysis.ModelDraw,
                        TwoToThreeGoalsProb = originalAnalysis.ModelGoals23,
                        AwayProb = originalAnalysis.ModelAwayWin,
                    };

                    await UpsertAnalysisAsync(fixtureId, bilingualResult, bilingualResult.En, "en", mathProbs, cancellationToken);
                    await UpsertAnalysisAsync(fixtureId, bilingualResult, bilingualResult.De, "de", mathProbs, cancellationToken);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                // After the save, not before: a failed save lands in the catch
                // below, and must not be reported as generated as well.
                // Count success only after the decision explanation is also ready.
                logger.LogInformation("[AiSync] Successfully called SaveChangesAsync for batch.");

                foreach (var (fixtureId, _) in results)
                {
                    await precomputeService.RecomputeFixtureAsync(fixtureId, cancellationToken);
                    if (aiService.SupportsDecisionExplanations)
                        await RefreshExplanationAsync(fixtureId, force, cancellationToken);
                }
                totalProcessed += results.Count;

                logger.LogInformation("[AiSync] Batch {Num} fully persisted and snapshots updated.", i + 1);
                
                // Rate limiting to respect quota
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (ExternalApiException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.PaymentRequired
                or System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable)
            {
                throw; // Surface account/configuration failures to persisted sync status.
            }
            catch (Exception ex)
            {
                failedIds.Add(chunkList[0].FixtureId);
                logger.LogError(ex, "[AiSync] Error processing batch starting at index {Idx}", i);
            }
        }

        if (failedIds.Count == 0)
            logger.LogInformation("All batches completed. Total matches analyzed and persisted: {Total}", totalProcessed);
        return Report();

        AiSyncReport Report() => new()
        {
            WindowStartUtc = startUtc,
            WindowEndUtc = endUtc,
            Candidates = fixtures.Count,
            AlreadyAnalyzed = skippedCount,
            Attempted = toAnalyze.Count + explanationAttempts,
            Generated = totalProcessed,
            Failed = failedIds.Count,
            FailedFixtureIds = failedIds,
        };
    }

    public async Task SyncSingleFixtureAsync(int fixtureId, bool force = false, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Running targeted AI sync for Fixture {FixtureId}.", fixtureId);

        // A narrative must contain text. Confidence metadata alone can survive
        // a malformed response and must not suppress regeneration forever.
        var opinionPrompt = aiService.OpinionPromptHash;
        var langCount = await dbContext.FixtureAnalyses
            .CountAsync(a => a.FixtureId == fixtureId
                             && (a.Lang == "en" || a.Lang == "de")
                             && a.Analysis != null && a.Analysis.Trim() != ""
                             && (opinionPrompt == null || a.AiPromptHash == opinionPrompt), cancellationToken);

        if (!force && langCount >= 2)
        {
            if (aiService.SupportsDecisionExplanations)
                await RefreshExplanationAsync(fixtureId, false, cancellationToken);
            logger.LogInformation("Targeted sync: existing AI opinion retained for Fixture {FixtureId}.", fixtureId);
            return;
        }

        var fixture = await dbContext.Fixtures.FirstOrDefaultAsync(f => f.Id == fixtureId, cancellationToken);
        if (fixture == null)
        {
            throw new KeyNotFoundException($"Fixture {fixtureId} not found.");
        }

        if (fixture.Date <= DateTimeOffset.UtcNow || fixture.Status is not ("NS" or "TBD"))
            throw new InvalidOperationException("AI forecasts can only be generated for upcoming fixtures.");

        var teams = await dbContext.Teams
            .Where(t => t.ApiId == fixture.HomeTeamId || t.ApiId == fixture.AwayTeamId)
            .ToDictionaryAsync(t => t.ApiId, t => t, cancellationToken);

        var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
        var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);
        if (homeTeam == null || awayTeam == null)
        {
            throw new InvalidOperationException($"Team context is missing for fixture {fixtureId}.");
        }

        // refresh: true — see the batch path: a warm cache returns
        // PoissonModel.Empty and the model would be sent all-zero probabilities.
        var analysis = await analysisService.AnalyzeFixtureAsync(fixture, "en", refresh: true, cancellationToken);
        var probs = analysis.Prediction ?? new WeightedPrediction();
        if (probs.HomeProb <= 0 && probs.Over25Prob <= 0)
            throw new InvalidOperationException($"No model probabilities available for fixture {fixtureId}.");
        var item = new AiBatchItem
        {
            FixtureId = fixture.Id,
            League    = analysis.LeagueName,
            HomeTeam  = homeTeam.Name,
            AwayTeam  = awayTeam.Name,
            HomeStats = analysis.TeamStats.Home,
            AwayStats = analysis.TeamStats.Away,
            ModelHomeWin = probs.HomeProb,
            ModelDraw = probs.DrawProb,
            ModelAwayWin = probs.AwayProb,
            ModelOver25 = probs.Over25Prob,
            ModelBTTS = probs.BTTSProb,
            ModelGoals23 = probs.TwoToThreeGoalsProb,
                    ModelBttsAndOver25 = analysis.Models.Poisson.IsValid ? analysis.Models.Poisson.BttsAndOver25 : null,
            OddsHomeWin = analysis.OddsHomeWin,
            OddsDraw = analysis.OddsDraw,
            OddsAwayWin = analysis.OddsAwayWin,
            OddsOver25 = analysis.OddsOver25,
            OddsBTTS = analysis.OddsBttsYes
        };

        await RequireUpcomingFixtureAsync(fixtureId, cancellationToken);
        var results = await aiService.AnalyzeBatchAsync([item], cancellationToken);
        if (results.Count == 1 && results.TryGetValue(fixtureId, out var bilingualResult) &&
            AiNarrativeIntegrity.InvalidResult(bilingualResult) is null)
        {
            await RequireUpcomingFixtureAsync(fixtureId, cancellationToken);
            var raw = analysis.RawPrediction ?? probs;
            await UpsertAnalysisAsync(fixture.Id, bilingualResult, bilingualResult.En, "en", raw, cancellationToken);
            await UpsertAnalysisAsync(fixture.Id, bilingualResult, bilingualResult.De, "de", raw, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await precomputeService.RecomputeFixtureAsync(fixtureId, cancellationToken);
            if (aiService.SupportsDecisionExplanations)
                await RefreshExplanationAsync(fixtureId, force, cancellationToken);
            logger.LogInformation("Successfully synced AI analysis for Fixture {FixtureId}.", fixtureId);
        }
        else
        {
            throw new ExternalApiException("AI narratives", $"AI returned no complete bilingual analysis for fixture {fixtureId}.");
        }
    }

    private async Task<bool> RefreshExplanationAsync(int fixtureId, bool force, CancellationToken ct)
    {
        await RequireUpcomingFixtureAsync(fixtureId, ct);
        var results = await precomputeService.RecomputeFixtureAsync(fixtureId, ct);
        if (!results.TryGetValue("en", out var snapshot) || snapshot.DecisionAudit is null)
            throw new InvalidOperationException($"No final decision is available for fixture {fixtureId}.");
        var fixture = await dbContext.Fixtures.AsNoTracking().SingleAsync(f => f.Id == fixtureId, ct);
        SoccerAi.Application.Services.LiveOddsPolicy.RefreshResponse(snapshot, fixture, DateTimeOffset.UtcNow);
        if (!force && DecisionExplanationPolicy.IsCurrent(snapshot, snapshot.DecisionExplanation)) return false;

        var input = DecisionExplanationPolicy.Input(snapshot);
        var explanation = await aiService.ExplainDecisionAsync(input, ct);
        if (DecisionExplanationPolicy.Invalid(explanation, input) is { } invalid)
            throw new InvalidDataException($"Invalid decision explanation: {invalid}.");
        await RequireUpcomingFixtureAsync(fixtureId, ct);
        // Prices or model inputs can change while the provider is answering.
        var latest = await precomputeService.RecomputeFixtureAsync(fixtureId, ct);
        fixture = await dbContext.Fixtures.AsNoTracking().SingleAsync(f => f.Id == fixtureId, ct);
        var current = latest["en"];
        SoccerAi.Application.Services.LiveOddsPolicy.RefreshResponse(current, fixture, DateTimeOffset.UtcNow);
        if (DecisionExplanationPolicy.Hash(DecisionExplanationPolicy.Input(current)) != DecisionExplanationPolicy.Hash(input))
            throw new InvalidOperationException("Decision changed during explanation generation; retry using the current inputs.");

        explanation!.InputHash = DecisionExplanationPolicy.Hash(input);
        var rows = await dbContext.FixtureAnalyses
            .Where(a => a.FixtureId == fixtureId && (a.Lang == "en" || a.Lang == "de")).ToListAsync(ct);
        foreach (var row in rows)
        {
            row.DecisionExplanationJson = System.Text.Json.JsonSerializer.Serialize(explanation);
            if (!latest.TryGetValue(row.Lang, out var response)) continue;
            SoccerAi.Application.Services.LiveOddsPolicy.RefreshResponse(response, fixture, DateTimeOffset.UtcNow);
            response.DecisionExplanation = explanation;
            DecisionExplanationPolicy.Refresh(response, row.Lang);
            row.SnapshotJson = AnalysisSnapshotSerializer.Serialize(response);
        }
        await dbContext.SaveChangesAsync(ct);
        return true;
    }

    private async Task RequireUpcomingFixtureAsync(int fixtureId, CancellationToken ct)
    {
        var current = await dbContext.Fixtures.AsNoTracking().Where(f => f.Id == fixtureId)
            .Select(f => new { f.Date, f.Status }).SingleOrDefaultAsync(ct);
        if (current == null || current.Date <= DateTimeOffset.UtcNow || current.Status is not ("NS" or "TBD"))
            throw new InvalidOperationException($"Fixture {fixtureId} is no longer upcoming; no pre-match AI forecast accepted.");
    }

    private async Task UpsertAnalysisAsync(int fixtureId, AiBilingualResult aiResult, AiLanguageBlock block, string lang, WeightedPrediction math, CancellationToken ct)
    {
        var existing = await dbContext.FixtureAnalyses
            .FirstOrDefaultAsync(a => a.FixtureId == fixtureId && a.Lang == lang, ct);

        if (existing != null)
        {
            existing.Lang                = lang;
            existing.Recommendation      = aiResult.Recommendation;
            existing.Confidence          = aiResult.Confidence;
            existing.PredictionReason    = block.PredictionReason ?? "";
            existing.Analysis            = block.Analysis ?? "";
            existing.ConsensusEvaluation = block.ConsensusEvaluation ?? "";
            existing.BttsSummary         = block.Summaries?.Btts ?? "";
            existing.Over25Summary       = block.Summaries?.Over25 ?? "";
            existing.Under25Summary      = block.Summaries?.Under25 ?? "";
            existing.HomeWinSummary      = block.Summaries?.HomeWin ?? "";
            existing.AwayWinSummary      = block.Summaries?.AwayWin ?? "";
            
            // MATH CACHE
            existing.HomeProb            = math.HomeProb;
            existing.DrawProb            = math.DrawProb;
            existing.AwayProb            = math.AwayProb;
            existing.Over25Prob          = math.Over25Prob;
            existing.BttsProb            = math.BTTSProb;
            existing.Goals23Prob         = math.TwoToThreeGoalsProb;
            
            // Unified AI Decision Layer
            existing.AiOver25Qualified    = aiResult.Over25Qualified;
            existing.AiBttsQualified      = aiResult.BttsQualified;
            existing.AiUnder25Qualified   = aiResult.Under25Qualified;
            existing.AiGoals23Qualified   = aiResult.Goals23Qualified;
            existing.AiBttsAndOver25Qualified = aiResult.BttsAndOver25Qualified;
            existing.AiHomeWinQualified   = aiResult.HomeWinQualified;
            existing.AiAwayWinQualified   = aiResult.AwayWinQualified;
            existing.AiBestBet            = aiResult.BestBet ?? "";
            existing.AiOverallConfidence  = aiResult.OverallConfidence;

            existing.AiGeneratedAtUtc = aiResult.GeneratedAtUtc;
            existing.AiModelVersion = aiResult.ModelVersion;
            existing.AiPromptHash = aiResult.PromptHash;
            existing.AiInputHash = aiResult.InputHash;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            dbContext.FixtureAnalyses.Add(new FixtureAnalysis
            {
                FixtureId           = fixtureId,
                Lang                = lang,
                Recommendation      = aiResult.Recommendation,
                Confidence          = aiResult.Confidence,
                PredictionReason    = block.PredictionReason ?? "",
                Analysis            = block.Analysis ?? "",
                ConsensusEvaluation = block.ConsensusEvaluation ?? "",
                BttsSummary         = block.Summaries?.Btts ?? "",
                Over25Summary       = block.Summaries?.Over25 ?? "",
                Under25Summary      = block.Summaries?.Under25 ?? "",
                HomeWinSummary      = block.Summaries?.HomeWin ?? "",
                AwayWinSummary      = block.Summaries?.AwayWin ?? "",
                
                // MATH CACHE
                HomeProb            = math.HomeProb,
                DrawProb            = math.DrawProb,
                AwayProb            = math.AwayProb,
                Over25Prob          = math.Over25Prob,
                BttsProb            = math.BTTSProb,
                Goals23Prob         = math.TwoToThreeGoalsProb,
                
                // Unified AI Decision Layer
                AiOver25Qualified   = aiResult.Over25Qualified,
                AiBttsQualified     = aiResult.BttsQualified,
                AiUnder25Qualified  = aiResult.Under25Qualified,
                AiGoals23Qualified  = aiResult.Goals23Qualified,
                AiBttsAndOver25Qualified = aiResult.BttsAndOver25Qualified,
                AiHomeWinQualified  = aiResult.HomeWinQualified,
                AiAwayWinQualified  = aiResult.AwayWinQualified,
                AiBestBet           = aiResult.BestBet ?? "",
                AiGeneratedAtUtc = aiResult.GeneratedAtUtc,
                AiModelVersion = aiResult.ModelVersion,
                AiPromptHash = aiResult.PromptHash,
                AiInputHash = aiResult.InputHash,
                AiOverallConfidence = aiResult.OverallConfidence
            });
        }
    }
}
