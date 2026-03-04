using Mediator.Net.Contracts;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Features.Automation;

public class RunDailySyncCommand(int season) : ICommand
{
    public int Season { get; } = season;
}
