namespace RagBackgroundWorker;

public record PromptMessage(Guid TaskId, Guid UserId, string Prompt);
