namespace IssueToPrAgent.Application.Analysis;

/// <summary>One model completion request: a system prompt and a user prompt for a given model.</summary>
public sealed record ModelRequest(string Model, string SystemPrompt, string UserPrompt);

/// <summary>The model's reply plus token usage (for cost/latency awareness).</summary>
public sealed record ModelReply(string Text, long InputTokens, long OutputTokens);

/// <summary>
/// A lean text-completion port. Deliberately has no tools and no way to act — the analyzer only
/// ever asks the model for text, so the model has no surface through which it could read outside
/// the repo, run a command, or change anything. That guarantee is the interface's shape, not a
/// prompt. Implementations are stateless per call, keeping the provider replaceable.
/// </summary>
public interface ILanguageModel
{
    Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}
