namespace Webboard.Web.Services;

public static class ExceptionDiagnostics {
    // Retain the exception chain, codes and stack without copying messages, data or request credentials.
    public static string Describe(Exception exception) {
        var chain = new List<string>();
        for (Exception? current = exception; current is not null && chain.Count < 8; current = current.InnerException) {
            var code = current switch {
                Npgsql.PostgresException postgres => postgres.SqlState,
                HttpRequestException http => http.StatusCode?.ToString(),
                System.Net.Sockets.SocketException socket => socket.SocketErrorCode.ToString(),
                _ => null
            };
            chain.Add($"{current.GetType().FullName} {code}");
        }
        var stack = exception.StackTrace ?? "";
        return string.Join(" -> ", chain) + "\n" + stack[..Math.Min(stack.Length, 16384)];
    }
}
