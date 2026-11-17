using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

// DEBUG ONLY. Captures bounded JSON payloads; never deploy on public endpoints.
public sealed class DevBridgeTraceMiddleware(RequestDelegate next, IHttpClientFactory clients)
{
    const int Limit = 16 * 1024;
    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString();
        context.Response.Headers["X-Correlation-ID"] = traceId;
        var sw = Stopwatch.StartNew();
        var requestJson = await ReadRequest(context);
        await Emit("server", $"{context.Request.Method} {context.Request.Path} started", "info", requestJson, "server-request");
        var original = context.Response.Body;
        await using var capture = new CaptureStream(original, Limit);
        context.Response.Body = capture;
        try
        {
            await next(context);
            var responseJson = IsJson(context.Response.ContentType) ? SafeJson(capture.Captured) : null;
            await Emit("server", $"HTTP {context.Response.StatusCode} ({sw.ElapsedMilliseconds} ms)", context.Response.StatusCode >= 400 ? "error" : "success", responseJson, "server-response");
        }
        catch (Exception ex)
        {
            await Emit("server", $"Exception: {ex.GetType().Name}", "error", null, "server-response");
            throw;
        }
        finally { context.Response.Body = original; }

        async Task Emit(string source, string message, string level, string? data, string stage)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await clients.CreateClient("DevBridgeTrace").PostAsJsonAsync("api/traces", new {
                    correlationId = traceId, source, message, level, stage, data, at = DateTimeOffset.UtcNow
                }, cts.Token);
            }
            catch { /* Diagnostic channel is optional. */ }
        }
    }

    static bool IsJson(string? type) => type?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
    static async Task<string?> ReadRequest(HttpContext context)
    {
        if (!IsJson(context.Request.ContentType) || context.Request.ContentLength > Limit) return null;
        try
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var chars = new char[Limit + 1];
            var count = await reader.ReadBlockAsync(chars, 0, chars.Length);
            context.Request.Body.Position = 0;
            return count > Limit ? null : SafeJson(new string(chars, 0, count));
        }
        catch { if (context.Request.Body.CanSeek) context.Request.Body.Position = 0; return null; }
    }
    static string? SafeJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > Limit) return null;
        try { using var doc = JsonDocument.Parse(json); return JsonSerializer.Serialize(Redact(doc.RootElement)); }
        catch { return null; }
    }
    static object? Redact(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(x => x.Name, x => Secret(x.Name) ? (object?)"[REDACTED]" : Redact(x.Value)),
        JsonValueKind.Array => e.EnumerateArray().Select(Redact).ToArray(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
    static bool Secret(string key) => new[] {"password","passwd","token","secret","authorization","apikey","api_key","credential","cookie"}.Any(s => key.Contains(s, StringComparison.OrdinalIgnoreCase));
    sealed class CaptureStream(Stream inner, int max) : Stream
    {
        readonly MemoryStream captured = new();
        public string Captured => Encoding.UTF8.GetString(captured.ToArray());
        void Save(ReadOnlySpan<byte> bytes) { var take = Math.Min(bytes.Length, max - (int)captured.Length); if (take > 0) captured.Write(bytes[..take]); }
        public override void Write(byte[] buffer, int offset, int count) { Save(buffer.AsSpan(offset,count)); inner.Write(buffer,offset,count); }
        public override async Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken ct) { Save(buffer.AsSpan(offset,count)); await inner.WriteAsync(buffer.AsMemory(offset,count),ct); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default) { Save(buffer.Span); await inner.WriteAsync(buffer,ct); }
        public override void Flush()=>inner.Flush(); public override Task FlushAsync(CancellationToken ct)=>inner.FlushAsync(ct);
        public override bool CanRead=>false; public override bool CanSeek=>false; public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException(); public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException(); public override long Seek(long o,SeekOrigin origin)=>throw new NotSupportedException(); public override void SetLength(long n)=>throw new NotSupportedException();
    }
}
