# DevBridge v0.4 — Trace integration

## 1. Windows DevBridge

Run DevBridge.exe. The receiver is on `http://127.0.0.1:5198/api/traces` and is bound to loopback only. The Trace tab polls new events every 1.5 seconds. This is a **debug-only** receiver: do not expose the port publicly. The event store is in memory and keeps up to 1000 events. Avoid logging secrets, tokens, personal data or raw JSON by default.

## 2. ASP.NET Core

Copy `integrations/aspnet/DevBridgeTrace.cs` into your API project. In `Program.cs`, before `app.MapControllers()` and after `app = builder.Build()`:

```csharp
#if DEBUG
builder.Services.AddHttpClient("DevBridgeTrace", c =>
    c.BaseAddress = new Uri("http://127.0.0.1:5198/")); // BEFORE builder.Build()
#endif
```

Then, after `builder.Build()` and before mapping controllers:

```csharp
#if DEBUG
app.UseMiddleware<DevBridgeTraceMiddleware>();
#endif
```

ASP.NET Core uses the incoming W3C `traceparent` to create `Activity.Current`. When the server is on another machine, 127.0.0.1 refers to **that server**, not the workstation. For remote hosts, configure a secure authenticated collector rather than opening the local DevBridge port.

## 3. Android / Retrofit

Copy `integrations/android/DevBridgeTraceInterceptor.kt` into your Android app, adjusting the `package` declaration. Use the OkHttp already present with Retrofit. Add the interceptor **only for debug builds**:

```kotlin
val builder = OkHttpClient.Builder()
if (BuildConfig.DEBUG) builder.addInterceptor(DevBridgeTraceInterceptor())
val client = builder.build()
val retrofit = Retrofit.Builder().baseUrl(baseUrl).client(client)
    .addConverterFactory(GsonConverterFactory.create()).build()
```

With a USB-connected device or emulator, run on Windows (replace the path):

```powershell
& "C:\Android\platform-tools\adb.exe" -s DEVICE_SERIAL reverse tcp:5198 tcp:5198
```

The Android app also needs INTERNET permission. If cleartext traffic is blocked, allow cleartext to 127.0.0.1 **only in debug configuration** (e.g. debug-only network security config). Do not relax production security.

Requests carry `traceparent` and `X-Correlation-ID`; server and Android events share the same trace ID. Trace will show HTTP send/receive and server start/end.

**DTO processing is not automatic.** The interceptor cannot see what your ViewModel does after Retrofit. To instrument that stage, pass the trace ID from the response header or request context to your own debug event and call `dto(traceId, "mapped", "items=15")`. Do not send DTO values containing secrets or personal data.

## Limitations

- This is a small, dependency-free integration, **not a full OpenTelemetry collector**.
- Network delivery is best effort. Trace events may be dropped if DevBridge is not running.
- Timestamps from Android and Windows can differ; events are sorted by their recorded receipt times.
- In this version `dto()` needs the trace ID supplied explicitly. A production-quality tracing SDK would propagate context through coroutine scopes.
- The collector is unauthenticated and should remain loopback-only; ADB reverse permits the connected Android device to reach it.
