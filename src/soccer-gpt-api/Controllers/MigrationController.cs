using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MigrationController(IExcelToSqliteMigrationService migrationService) : ControllerBase
{
    [HttpPost("migrate")]
    public async Task<IActionResult> Migrate(CancellationToken cancellationToken)
    {
        var result = await migrationService.MigrateAsync(cancellationToken);
        return Ok(result);
    }
    
    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequest("File is empty.");

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await migrationService.MigrateStreamAsync(stream, file.FileName, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}
