using System.Diagnostics;
using System.Net.Http.Json;

// Add this file to your ASP.NET Core web project. Debug only; no NuGet packages needed.
public sealed class DevBridgeTraceMiddleware(RequestDelegate next, IHttpClientFactory clients)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // ASP.NET Core normally creates Activity from the incoming traceparent header.
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        context.Response.Headers["X-Correlation-ID"] = traceId;
        var sw = Stopwatch.StartNew();
        _ = Emit("server", $"{context.Request.Method} {context.Request.Path} started", "info");
        try
        {
            await next(context);
            _ = Emit("server", $"HTTP {context.Response.StatusCode} ({sw.ElapsedMilliseconds} ms)", context.Response.StatusCode >= 400 ? "error" : "success");
        }
        catch (Exception ex)
        {
            _ = Emit("server", $"Exception: {ex.GetType().Name}", "error");
            throw;
        }
        async Task Emit(string source, string message, string level)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await clients.CreateClient("DevBridgeTrace").PostAsJsonAsync("api/traces", new {
                    correlationId = traceId, source, message, level, at = DateTimeOffset.UtcNow
                }, cts.Token);
            }
            catch { /* Debug instrumentation must never break the API. */ }
        }
    }
}
