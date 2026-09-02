using Anthropic;
using Anthropic.Models.Messages;
using IssueToPrAgent.Application.Analysis;

namespace IssueToPrAgent.Infrastructure.Analysis;

/// <summary>
/// <see cref="ILanguageModel"/> over the official Anthropic SDK. A single completion per call,
/// no tools — the analyzer only produces text. Thinking is left unset so the adapter works across
/// the whole model matrix. The client is created lazily so a missing API key only fails an actual
/// analysis call, not construction.
/// </summary>
public sealed class AnthropicLanguageModel(AnthropicClient? client = null, long maxTokens = 2048) : ILanguageModel
{
    private readonly Lazy<AnthropicClient> _client = new(() => client ?? new AnthropicClient());

    public async Task<ModelReply> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var response = await _client.Value.Messages.Create(new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = maxTokens,
            System = request.SystemPrompt,
            Messages = [new() { Role = Role.User, Content = request.UserPrompt }],
        }, cancellationToken: cancellationToken);

        var text = string.Join("\n", response.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text));

        return new ModelReply(text, response.Usage.InputTokens, response.Usage.OutputTokens);
    }
}
