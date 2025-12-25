using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IPoissonFailureFilters
{
    FilterResult ApplyOver25Filters(MatchContext ctx);
    FilterResult ApplyBTTSFilters(MatchContext ctx);
}
