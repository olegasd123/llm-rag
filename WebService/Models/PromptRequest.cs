using Swashbuckle.AspNetCore.Annotations;

namespace RagWebService.Models;

[SwaggerSchema(Description = "Request payload for submitting a prompt.")]
public record PromptRequest(string Prompt);
