using System.Globalization;
using System.Text;

namespace SoccerAi.Application.Services.Odds;

/// <summary>A team we could map an external name onto.</summary>
public sealed record TeamCandidate(int ApiId, string Name, string? ShortName);

/// <summary>How a name was resolved — recorded so an import can be audited.</summary>
public sealed record TeamNameMatch(int ApiId, string MatchedName, double Similarity, string Method)
{
    public const string Exact = "exact";
    public const string Alias = "alias";
    public const string TokenPrefix = "token_prefix";
    public const string Fuzzy = "fuzzy";
}

/// <summary>
/// Maps team names from an external feed onto the teams in this database.
///
/// This is the whole difficulty of importing third-party football data. The
/// same club appears as "Man United", "Manchester Utd" and "Manchester United"
/// depending on the source, while "Nott'm Forest" resembles nothing at all.
///
/// The rule that governs everything here: <b>never guess</b>. An unmatched name
/// is reported so a human can add an alias. A wrong match writes one club's
/// prices onto another club's fixture, which corrupts the backtest silently and
/// in a way nobody would ever notice.
/// </summary>
public static class TeamNameMatcher
{
    /// <summary>
    /// Club-type words that carry no identity. Digits are deliberately kept:
    /// Schalke 04, Hannover 96 and 1899 Hoffenheim need them.
    /// </summary>
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal)
    {
        "fc", "afc", "cf", "sc", "ac", "as", "ss", "ssc", "us", "usl", "cd", "ud", "rc", "rcd",
        "sd", "sv", "tsv", "tsg", "vfl", "vfb", "bsc", "fsv", "msv", "spvgg", "sp", "sg",
        "calcio", "club", "futbol", "football", "deportivo", "sportiva", "sporting",
        "the", "de", "del", "di", "of"
    };

    /// <summary>
    /// Names no amount of string similarity will resolve, keyed by normalized
    /// external name. Kept small on purpose: the import report is what tells you
    /// which entries to add, rather than guessing in advance.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["nottm forest"] = "nottingham forest",
        ["man united"] = "manchester united",
        ["man utd"] = "manchester united",
        ["man city"] = "manchester city",
        ["sheffield weds"] = "sheffield wednesday",
        ["wolves"] = "wolverhampton wanderers",
        ["west brom"] = "west bromwich albion",
        ["qpr"] = "queens park rangers",
        ["newcastle"] = "newcastle united",
        ["leeds"] = "leeds united",
        ["west ham"] = "west ham united",
        ["tottenham"] = "tottenham hotspur",
        ["brighton"] = "brighton hove albion",
        ["leicester"] = "leicester city",
        ["norwich"] = "norwich city",
        ["stoke"] = "stoke city",
        ["swansea"] = "swansea city",
        ["cardiff"] = "cardiff city",
        ["hull"] = "hull city",
        ["birmingham"] = "birmingham city",
        ["coventry"] = "coventry city",
        ["derby"] = "derby county",
        ["ipswich"] = "ipswich town",
        ["luton"] = "luton town",
        ["preston"] = "preston north end",
        ["ath madrid"] = "atletico madrid",
        ["ath bilbao"] = "athletic club",
        ["sociedad"] = "real sociedad",
        ["betis"] = "real betis",
        ["celta"] = "celta vigo",
        ["espanol"] = "espanyol",
        ["vallecano"] = "rayo vallecano",
        ["la coruna"] = "deportivo la coruna",
        ["ein frankfurt"] = "eintracht frankfurt",
        ["bayern munich"] = "bayern munchen",
        ["dortmund"] = "borussia dortmund",
        ["mgladbach"] = "borussia monchengladbach",
        ["m gladbach"] = "borussia monchengladbach",
        ["leverkusen"] = "bayer leverkusen",
        ["hertha"] = "hertha bsc",
        ["stuttgart"] = "vfb stuttgart",
        ["wolfsburg"] = "vfl wolfsburg",
        ["hoffenheim"] = "1899 hoffenheim",
        ["st pauli"] = "fc st pauli",
        ["fc koln"] = "1 fc koln",
        ["inter"] = "inter milan",
        ["milan"] = "ac milan",
        ["verona"] = "hellas verona",
        ["roma"] = "as roma",
        ["napoli"] = "ssc napoli",
        ["juventus"] = "juventus turin",
        ["paris sg"] = "paris saint germain",
        ["st etienne"] = "saint etienne",
        ["marseille"] = "olympique marseille",
        ["lyon"] = "olympique lyonnais"
    };

    /// <summary>
    /// Strips accents, punctuation and club-type words so that cosmetic spelling
    /// differences stop being differences at all.
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else sb.Append(' ');
        }

        var tokens = sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !NoiseTokens.Contains(t))
            .ToList();

        // A name made only of noise ("FC") keeps its original words rather than
        // collapsing to nothing, which would match everything.
        if (tokens.Count == 0)
            tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        return string.Join(' ', tokens);
    }

    /// <summary>
    /// Resolves an external name against the teams known to play in that league.
    /// Returns null rather than a doubtful match.
    /// </summary>
    public static TeamNameMatch? Match(
        string externalName, IReadOnlyCollection<TeamCandidate> candidates, double minSimilarity)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var normalized = Normalize(externalName);
        if (normalized.Length == 0 || candidates.Count == 0) return null;

        // The feed's own name is tried before any alias. An alias that does not
        // correspond to this database's spelling would otherwise *replace* a
        // name that already matched perfectly and turn a hit into a miss.
        var exact = FindExact(normalized, candidates);
        if (exact is not null) return exact;

        var hasAlias = Aliases.TryGetValue(normalized, out var alias);
        if (hasAlias)
        {
            var aliased = FindExact(alias!, candidates);
            if (aliased is not null)
                return aliased with { Similarity = 1.0, Method = TeamNameMatch.Alias };
        }

        // Fuzzy over both spellings, so a non-matching alias costs nothing.
        string[] forms = hasAlias ? [normalized, alias!] : [normalized];
        TeamNameMatch? best = null;

        foreach (var candidate in candidates)
        {
            foreach (var candidateName in NamesOf(candidate))
            {
                var candidateNormalized = Normalize(candidateName);
                if (candidateNormalized.Length == 0) continue;

                foreach (var form in forms)
                {
                    var (similarity, method) = Score(form, candidateNormalized);
                    if (similarity < minSimilarity) continue;
                    if (best is not null && similarity <= best.Similarity) continue;

                    best = new TeamNameMatch(candidate.ApiId, candidateName, similarity, method);
                }
            }
        }

        return best;
    }

    // ── Scoring ──────────────────────────────────────────────────────────────

    private static (double Similarity, string Method) Score(string a, string b)
    {
        if (TokensArePrefixes(a, b) || TokensArePrefixes(b, a))
            return (0.95, TeamNameMatch.TokenPrefix);

        return (Similarity(a, b), TeamNameMatch.Fuzzy);
    }

    /// <summary>
    /// Every token of the shorter name begins a distinct token of the longer
    /// one: "man united" against "manchester united". Abbreviation is the
    /// commonest difference between feeds, and edit distance handles it badly.
    /// </summary>
    private static bool TokensArePrefixes(string shorter, string longer)
    {
        var shortTokens = shorter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var longTokens = longer.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (shortTokens.Length == 0 || shortTokens.Length > longTokens.Count) return false;

        foreach (var token in shortTokens)
        {
            // Single letters would match far too much to be evidence.
            if (token.Length < 2) return false;

            var index = longTokens.FindIndex(t => t.StartsWith(token, StringComparison.Ordinal));
            if (index < 0) return false;

            longTokens.RemoveAt(index);
        }

        return true;
    }

    public static double Similarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        var distance = Levenshtein(a, b);
        return 1.0 - (double)distance / Math.Max(a.Length, b.Length);
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static TeamNameMatch? FindExact(string normalized, IEnumerable<TeamCandidate> candidates)
    {
        foreach (var candidate in candidates)
            foreach (var name in NamesOf(candidate))
                if (Normalize(name) == normalized)
                    return new TeamNameMatch(candidate.ApiId, name, 1.0, TeamNameMatch.Exact);

        return null;
    }

    private static IEnumerable<string> NamesOf(TeamCandidate candidate)
    {
        yield return candidate.Name;
        if (!string.IsNullOrWhiteSpace(candidate.ShortName)) yield return candidate.ShortName;
    }
}
