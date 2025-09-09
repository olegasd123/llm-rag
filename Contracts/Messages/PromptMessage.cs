namespace Contracts.Messages;

public record PromptMessage(Guid TaskId, Guid UserId, string Prompt);

