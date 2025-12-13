
using Mediator.Net.Contracts;
using soccer_gpt_application.Models;
using soccer_gpt_application.Models.Llm;

namespace soccer_gpt_application.Features.Predictions.Queries;

public class GetPredictionsQuery : IRequest
{
    public int Offset { get; set; } = 0;
    public int Limit { get; set; } = 10;
}

public class GetPredictionsResponse : IResponse
{
    public PagedResponse<LlmMatchDataset> Data { get; set; } = new();
}
