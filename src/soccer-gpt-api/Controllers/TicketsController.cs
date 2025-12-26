using Mediator.Net;
using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Features.Tickets.Queries;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TicketsController : ControllerBase
{
    private readonly ILogger<TicketsController> _logger;
    private readonly IMediator _mediator;
    
    public TicketsController(
        ILogger<TicketsController> logger,
        IMediator mediator)
    {
        _logger = logger;
        _mediator = mediator;
    }

    [HttpGet("generate")]
    public async Task<IActionResult> GenerateTickets(
        [FromQuery] int gamesPerTicket = 3, 
        [FromQuery] int numTickets = 3)
    {
        _logger.LogInformation("Generating {NumTickets} tickets with {GamesPerTicket} games each", 
            numTickets, gamesPerTicket);
        
        try
        {
            var query = new GenerateTicketsQuery
            {
                MinGamesPerTicket = gamesPerTicket,
                MaxTickets = numTickets
            };
            
            var response = await _mediator.RequestAsync<GenerateTicketsQuery, GenerateTicketsResponse>(query);
            
            return Ok(new 
            { 
                generated_at = DateTime.Now,
                strategy = response.Strategy,
                total_candidates = response.TotalCandidates,
                ticket_count = response.Tickets.Count,
                tickets = response.Tickets
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ticket Generation Failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
