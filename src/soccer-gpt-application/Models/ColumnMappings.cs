using System.Data;

namespace soccer_gpt_application.Models;


public class ColumnMappings
{
    public string? Div { get; }
    public string? HomeTeam { get; }
    public string? AwayTeam { get; }
    public string? Date { get; }
    public string? Time { get; }
    public string? FtHg { get; }
    public string? FtAg { get; }
    public string? FTR { get; }
    public string? htHg { get; }
    public string? HtAg { get; }
    public string? HTR { get; }
    public string? Referee { get; }

    public bool IsValid => Div != null && HomeTeam != null && AwayTeam != null && Date != null;

    public ColumnMappings(DataColumnCollection cols)
    {
        Div = Get(cols, "Div");
        HomeTeam = Get(cols, "HomeTeam");
        AwayTeam = Get(cols, "AwayTeam");
        Date = Get(cols, "Date");
        Date = Get(cols, "Date");
        Time = Get(cols, "Time", "KickOff", "Ko", "Time (GMT)", "HomeTeamTime");
        FtHg = Get(cols, "FTHG", "HG");
        FtAg = Get(cols, "FTAG", "AG");
        FTR = Get(cols, "FTR", "Res");
        htHg = Get(cols, "HTHG");
        HtAg = Get(cols, "HTAG");
        HTR = Get(cols, "HTR");
        Referee = Get(cols, "Referee", "Ref");
    }

    private static string? Get(DataColumnCollection columns, params string[] candidates)
        => columns.Cast<DataColumn>()
            .Select(c => c.ColumnName)
            .FirstOrDefault(name => candidates.Contains(name, StringComparer.OrdinalIgnoreCase));
}