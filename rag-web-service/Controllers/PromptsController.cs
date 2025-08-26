using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RagWebService.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace RagWebService.Controllers;

[ApiController]
[Route("prompts")]
[SwaggerTag("Handles submission of prompts and retrieval of generated results.")]
public class PromptsController : ControllerBase
{
    private readonly IPublishEndpoint _publisher;

    public PromptsController(IPublishEndpoint publisher)
    {
        _publisher = publisher;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SubmitPrompt([FromBody] PromptRequest request)
    {
        var taskId = Guid.NewGuid().ToString();
        await _publisher.Publish(new { TaskId = taskId, Prompt = request.Prompt });
        return Accepted(new { taskId });
    }

    [HttpGet("{id}")]
    [Authorize]
    public IActionResult GetResult(string id)
    {
        // Placeholder for result retrieval logic
        return NotFound();
    }
}
