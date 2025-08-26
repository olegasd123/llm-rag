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
    [SwaggerOperation(Summary = "Submit a prompt", Description = "Queues a prompt for processing and returns a task identifier.")]
    public async Task<IActionResult> SubmitPrompt([FromBody] PromptRequest request)
    {
        var taskId = Guid.NewGuid().ToString();
        await _publisher.Publish(new { TaskId = taskId, Prompt = request.Prompt });
        return Accepted(new { taskId });
    }

    [HttpGet("{id}")]
    [Authorize]
    [SwaggerOperation(Summary = "Get prompt result", Description = "Retrieves the generated result for the specified task id.")]
    public IActionResult GetResult(string id)
    {
        // Placeholder for result retrieval logic
        return NotFound();
    }
}
