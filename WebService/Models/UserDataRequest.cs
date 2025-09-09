using Swashbuckle.AspNetCore.Annotations;

namespace RagWebService.Models;

[SwaggerSchema(Description = "Request payload for setting user-specific context data.")]
public record UserDataRequest(string Data);

