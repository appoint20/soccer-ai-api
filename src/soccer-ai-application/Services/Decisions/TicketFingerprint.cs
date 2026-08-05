using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SoccerAi.Application.Services.Decisions;

/// <summary>
/// Stable identity for a published ticket, so recording a board twice records
/// it once.
///
/// The fingerprint covers what makes a ticket the same bet — the board date,
/// its kind, and its legs — and deliberately excludes odds. Prices move between
/// publication and kickoff; that does not turn a ticket into a different one,
/// and including the price would let a drifting line quietly create a second
/// row for a bet the customer only ever saw once.
/// </summary>
public static class TicketFingerprint
{
    public static string Compute(DateTimeOffset boardDateUtc, string kind, IEnumerable<TicketLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);

        var parts = new List<string>
        {
            boardDateUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            kind
        };

        // Legs are ordered so that the same set always hashes identically,
        // whatever order the builder happened to emit them in.
        parts.AddRange(legs
            .Select(l => $"{l.FixtureId}|{l.Market}|{l.Selection}")
            .OrderBy(s => s, StringComparer.Ordinal));

        var canonical = string.Join(";", parts);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
