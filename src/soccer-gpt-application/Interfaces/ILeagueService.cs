
namespace soccer_gpt_application.Interfaces;

public interface ILeagueService
{
    string GetLeagueNameFromCode(string code);
    bool IsLeagueSupported(string code);
}
