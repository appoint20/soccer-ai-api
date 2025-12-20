using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces
{
    public interface IGeminiService
    {
        Task<AnalyzedMatchDto> AnalyzeMatchAsync(AnalyzedMatchDto input);
        Task<List<GeminiTicketResponse>> GenerateTicketsAsync(List<AnalyzedMatchDto> candidates);
    }
}
