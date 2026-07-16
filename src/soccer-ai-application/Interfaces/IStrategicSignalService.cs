using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Computes the strategic signal catalog for a fixture from pre-kickoff DB data.
/// Signals gate decisions (confirm/veto) — they NEVER modify probabilities.
/// </summary>
public interface IStrategicSignalService
{
    Task<StrategicSignals> ComputeAsync(
        Fixture fixture, PoissonModel? dcModel, CancellationToken ct = default);
}
