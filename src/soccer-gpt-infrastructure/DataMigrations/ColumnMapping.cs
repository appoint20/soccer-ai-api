using System.Data;

namespace soccer_gpt_infrastructure.DataMigrations;


internal class ColumnMappings(DataColumnCollection cols)
{
    public string? Div { get; } = Get(cols, "Div");
    public string? HomeTeam { get; } = Get(cols, "HomeTeam");
    public string? AwayTeam { get; } = Get(cols, "AwayTeam");
    public string? Date { get; } = Get(cols, "Date");
    public string? Time { get; } = Get(cols, "Time");
    public string? FtHg { get; } = Get(cols, "FTHG");
    public string? FtAg { get; } = Get(cols, "FTAG");
    public string? FTR { get; } = Get(cols, "FTR");
    public string? htHg { get; } = Get(cols, "HTHG");
    public string? HtAg { get; } = Get(cols, "HTAG");
    public string? HTR { get; } = Get(cols, "HTR");
    public string? Referee { get; } = Get(cols, "Referee", "Ref");

    public bool IsValid => Div != null && HomeTeam != null && AwayTeam != null && Date != null;

    private static string? Get(DataColumnCollection columns, params string[] candidates)
    {
        return (
            from DataColumn col in columns 
            where candidates.Any(candidate => string.Equals(col.ColumnName, candidate, StringComparison.OrdinalIgnoreCase)) 
            select col.ColumnName)
            .FirstOrDefault();
    }
}