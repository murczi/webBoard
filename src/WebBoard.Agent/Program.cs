using System.Net.Sockets;
using WebBoard.Agent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
builder.Services.AddSingleton<AgentSecurity>();
builder.Services.AddSingleton<DockerSocketClient>();
builder.Services.AddSingleton<SystemdClient>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<AgentOperations>();

var app = builder.Build();
var security = app.Services.GetRequiredService<AgentSecurity>();
app.Use(async (context, next) => {
    if (!security.Authenticate(context.Request.Headers.Authorization.ToString())) {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    if (!context.Request.IsHttps && !builder.Configuration.GetValue<bool>("Security:AllowHttpOverEncryptedTransport")) {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    await next(context);
});

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

app.MapGet("/systemd/services", async (
        SystemdClient systemd,
        CancellationToken cancellationToken) => {
    try {
        return Results.Ok(await systemd.GetServicesAsync(cancellationToken));
    }
    catch (InvalidOperationException exception) {
        return Results.Problem(
            exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
        return Results.Problem(
            "The systemd request timed out.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
})
   .WithName("GetSystemdServices")
   .WithTags("systemd");

app.MapGet("/systemd/services/{serviceName}/status", async (
        string serviceName,
        SystemdClient systemd,
        CancellationToken cancellationToken) => {
    try {
        var status = await systemd.GetServiceStatusAsync(serviceName, cancellationToken);
        return status is null ? Results.NotFound() : Results.Ok(status);
    }
    catch (InvalidOperationException exception) {
        return Results.Problem(
            exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
        return Results.Problem(
            "The systemd request timed out.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
})
   .WithName("GetSystemdServiceStatus")
   .WithTags("systemd");

app.MapGet("/capabilities", async (string kind, string target, AgentOperations operations, DockerSocketClient docker, CancellationToken token) => {
    if (kind == "Docker") {
        try {
            var container = await docker.GetContainerStatusAsync(target, token);
            return Results.Ok(container is null ? Array.Empty<string>() : new[] { "start", "stop", "restart" }
                .Where(operation => operations.Allows("Docker", container.Id, operation) || operations.Allows("Docker", container.Name, operation)).ToArray());
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or InvalidOperationException) { return Results.StatusCode(503); }
    }
    return Results.Ok(kind == "Systemd" ? new[] { "start", "stop", "restart", "enable", "disable" }
        .Where(operation => operations.Allows("Systemd", target, operation)).ToArray() : Array.Empty<string>());
});
app.MapPost("/operations", async (OperationRequest request, AgentOperations operations, IHostApplicationLifetime lifetime) => {
    try {
        return Results.Ok(await operations.ExecuteAsync(request, lifetime.ApplicationStopping));
    }
    catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
    catch (ArgumentException) { return Results.BadRequest(new { message = "Invalid operation or configuration." }); }
    catch (InvalidOperationException) { return Results.Conflict(new { message = "Another operation is running." }); }
    catch (OperationCanceledException) { return Results.Json(new OperationResult(false, "Operation timed out; verify target state before retrying.", TimedOut: true), statusCode: 504); }
    catch (HttpRequestException) { return Results.Json(new OperationResult(false, "Docker communication failed; verify target state before retrying."), statusCode: 502); }
});
app.Run();

public partial class Program;
