using System.Net.Sockets;
using WebBoard.Agent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<DockerSocketClient>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new
   {
       Status = "Healthy",
       Service = "WebBoard.Agent"
   }))
   .WithName("GetHealth")
   .WithTags("Health");

app.MapGet("/docker/containers", async (
        DockerSocketClient docker,
        CancellationToken cancellationToken) => {
    try {
        return Results.Ok(await docker.GetContainersAsync(cancellationToken));
    }
    catch (Exception exception) when (exception is HttpRequestException or SocketException) {
        return Results.Problem(
            "Docker could not be reached through its Unix socket.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
        return Results.Problem(
            "The Docker socket request timed out.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
})
   .WithName("GetDockerContainers")
   .WithTags("Docker");

app.MapGet("/docker/containers/{containerId}/status", async (
        string containerId,
        DockerSocketClient docker,
        CancellationToken cancellationToken) => {
    try {
        var status = await docker.GetContainerStatusAsync(containerId, cancellationToken);
        return status is null ? Results.NotFound() : Results.Ok(status);
    }
    catch (Exception exception) when (exception is HttpRequestException or SocketException) {
        return Results.Problem(
            "Docker could not be reached through its Unix socket.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
        return Results.Problem(
            "The Docker socket request timed out.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
})
   .WithName("GetDockerContainerStatus")
   .WithTags("Docker");

app.Run();
