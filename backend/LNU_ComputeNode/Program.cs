using LNU_ComputeNode;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<NodeOptions>(builder.Configuration.GetSection(NodeOptions.SectionName));
builder.Services.AddSingleton<GaussSolver>();
builder.Services.AddSingleton<ComputeRegistry>();
builder.Services.AddHealthChecks();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<NodeOptions>>().Value;

app.MapPost("/compute", (ComputeRequest request, ComputeRegistry registry) =>
{
    if (request.Size < options.MinMatrixSize || request.Size > options.MaxMatrixSize)
    {
        return Results.BadRequest(new
        {
            message = $"Size must be between {options.MinMatrixSize} and {options.MaxMatrixSize}."
        });
    }

    return registry.TryStart(request.Id, request.Size, request.Seed) switch
    {
        StartResult.Started => Results.Accepted($"/compute/{request.Id}", registry.Find(request.Id)!.ToResponse(options.Id)),
        StartResult.AlreadyExists => Results.Conflict(new { message = "A task with this id already exists." }),
        _ => Results.Json(new { message = "All compute slots are busy." }, statusCode: StatusCodes.Status503ServiceUnavailable)
    };
});

app.MapGet("/compute/{id:guid}", (Guid id, ComputeRegistry registry) =>
{
    var task = registry.Find(id);
    return task == null ? Results.NotFound() : Results.Ok(task.ToResponse(options.Id));
});

app.MapDelete("/compute/{id:guid}", (Guid id, ComputeRegistry registry) =>
{
    var task = registry.Cancel(id);
    return task == null ? Results.NotFound() : Results.Ok(task.ToResponse(options.Id));
});

app.MapGet("/status", (ComputeRegistry registry) => Results.Ok(new
{
    nodeId = options.Id,
    runningJobs = registry.RunningJobs,
    maxConcurrentJobs = options.MaxConcurrentJobs
}));

app.MapHealthChecks("/health");

app.Run();

public record ComputeRequest(Guid Id, int Size, int Seed);
