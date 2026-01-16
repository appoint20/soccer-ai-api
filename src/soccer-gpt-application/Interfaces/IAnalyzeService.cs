using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IAnalyzeService
{
    Task<List<AnalysisDto>> AnalyzeUpcomingAsync(DateTime date, int offset = 0, int limit = 50);
}