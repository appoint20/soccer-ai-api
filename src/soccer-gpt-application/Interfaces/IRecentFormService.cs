using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IRecentFormService
{
    FormStats CalculateRecentForm(string team, List<HistoricalMatchDto> history, bool isHome);
}
