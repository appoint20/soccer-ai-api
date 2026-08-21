using System.Text.Json.Serialization;
using FluentValidation;
using Mediator.Net.Contracts;

namespace SoccerAi.Application.Features.Picks;

/// <summary>One selection the user ticked in the builder.</summary>
public sealed record CustomTicketLegRequest
{
    [JsonPropertyName("fixture_id")] public int FixtureId { get; init; }

    /// <summary>Market key as it appears in <c>decision_audit.markets[]</c>.</summary>
    [JsonPropertyName("market")] public string Market { get; init; } = "";
}

/// <summary>
/// Prices a user-assembled slip.
/// </summary>
/// <remarks>
/// Exists because the client can multiply prices exactly but cannot compute a
/// joint probability. Multiplying leg probabilities assumes independence, which
/// is false for two markets on one fixture — Over 2.5 and BTTS are strongly
/// correlated — so EV and Kelly derived that way would be confidently wrong.
///
/// Stateless: nothing is saved, and the slip stays on the device.
/// </remarks>
public sealed class PriceCustomTicketQuery : IRequest
{
    [JsonPropertyName("legs")] public IReadOnlyList<CustomTicketLegRequest> Legs { get; init; } = [];

    [JsonIgnore] public string? Language { get; set; }
}

public sealed class PriceCustomTicketQueryValidator : AbstractValidator<PriceCustomTicketQuery>
{
    /// <summary>
    /// Beyond this the slip is not a bet anyone places, and the joint
    /// probability is small enough that rounding dominates it.
    /// </summary>
    public const int MaxLegs = 8;

    public PriceCustomTicketQueryValidator()
    {
        RuleFor(q => q.Legs)
            .NotEmpty().WithMessage("A ticket needs at least one leg.");

        RuleFor(q => q.Legs)
            .Must(l => l.Count <= MaxLegs)
            .WithMessage($"A ticket may have at most {MaxLegs} legs.");

        RuleForEach(q => q.Legs).ChildRules(leg =>
        {
            leg.RuleFor(l => l.FixtureId).GreaterThan(0)
                .WithMessage("'fixture_id' must be a positive fixture id.");
            leg.RuleFor(l => l.Market).NotEmpty()
                .WithMessage("'market' is required on every leg.");
        });
    }
}

/// <summary>
/// The priced slip, or the reason it could not be priced.
/// </summary>
/// <remarks>
/// The failure is carried rather than thrown so the endpoint can answer 400
/// with a message naming the offending leg, which is what the builder shows.
/// </remarks>
public sealed record PriceCustomTicketResponse : IResponse
{
    [JsonPropertyName("ticket")] public TicketDto? Ticket { get; init; }

    /// <summary>Null on success.</summary>
    [JsonPropertyName("error")] public string? Error { get; init; }

    public static PriceCustomTicketResponse Fail(string error) => new() { Error = error };
}
