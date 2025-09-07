namespace RagBackgroundWorker;

// Message contract shared with the background worker consumer
public record PromptMessage(Guid TaskId, Guid UserId, string Prompt);
