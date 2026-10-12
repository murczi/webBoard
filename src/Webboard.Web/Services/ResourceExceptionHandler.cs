namespace Webboard.Web.Services;

using Domain.Services;
using Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Diagnostics;

public sealed class ResourceExceptionHandler : IExceptionHandler {
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token) {
        var failure = exception switch {
            UnauthorizedAccessException => (403, "Permission denied", "forbidden"),
            KeyNotFoundException => (404, "The selected item was not found", "not_found"),
            ResourceConflictException => (409, "Reload and review the latest data", "conflict"),
            OperationStoreException => (503, "Operations are temporarily unavailable", "unavailable"),
            _ => (0, "", "")
        };
        if (failure.Item1 == 0) return false;
        context.Response.StatusCode = failure.Item1;
        await context.Response.WriteAsJsonAsync(new { type = "about:blank", title = failure.Item2,
            status = failure.Item1, code = failure.Item3, traceId = context.TraceIdentifier }, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: token);
        return true;
    }
}
