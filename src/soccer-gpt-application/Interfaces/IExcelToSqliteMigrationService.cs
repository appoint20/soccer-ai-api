using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IExcelToSqliteMigrationService
{
    Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default);
    Task<MigrationResult> MigrateStreamAsync(Stream stream, string filename, CancellationToken cancellationToken = default);
}
