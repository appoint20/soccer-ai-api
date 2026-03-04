using Mediator.Net.Contracts;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Features.Verification;

/// <summary>
/// Query to retrieve paginated fixture list for verification.
/// </summary>
public record GetFixturesVerificationQuery(int Limit = 50, int Offset = 0) : IRequest;

/// <summary>
/// Query to retrieve paginated team list for verification.
/// </summary>
public record GetTeamsVerificationQuery(int Limit = 50, int Offset = 0, int? LeagueId = null) : IRequest;

/// <summary>
/// Command to sync fixtures for a specific league.
/// </summary>
public record SyncLeagueFixturesCommand(int LeagueId, int Season) : ICommand;

/// <summary>
/// Command to sync standings for a specific league.
/// </summary>
public record SyncLeagueStandingsCommand(int LeagueId, int Season) : ICommand;
