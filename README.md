# DevBridge — Windows developer console

Dark minimalist React + ASP.NET Core 10 developer tool for ASP.NET API and Android Logcat debugging.

## Build on Windows

Prerequisites: .NET 10 SDK, Node.js 22+, Android SDK Platform Tools (adb.exe), network access to Swagger.

Open PowerShell in the project folder and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-windows.ps1
.\dist\DevBridge.exe --open
```

Browser: http://127.0.0.1:5198. The published EXE contains React assets. First build needs NuGet and npm access. Development: run `run-dev.ps1` then open http://127.0.0.1:5173.

## Features

- Workspace: OpenAPI/Swagger endpoints, request body, send HTTP request, response/status/timing/correlation ID.
- Logcat: choose ADB device, stream logs, filter severity, text and optional Android package, clear entries.
- Trace: group events by correlation ID and show timeline. Manual requests create traces automatically.
- Profiles: local/production URLs and Bearer tokens, choose environment in top bar. Configure explicit adb.exe path (no PATH dependency).
- Profile tokens protected using Windows DPAPI (CurrentUser). Data stored in `%LOCALAPPDATA%\DevBridge\settings.json`.

## Integrating Android and server tracing

DevBridge cannot magically inspect Retrofit DTOs or server internals. For those stages, add debug-only instrumentation to your Android app and optionally ASP.NET middleware. Both should use the same correlation ID.

On Android, add an OkHttp Interceptor that attaches `X-Correlation-ID` to outbound requests, and log a trace event after receiving HTTP and after mapping the DTO. In a debug build only, POST an event to `http://<WORKSTATION_IP>:5198/api/traces` with:

```json
{"correlationId":"same-id-as-http-request","source":"Android / Retrofit","message":"DTO parsed","level":"success","data":"{...}"}
```

**IMPORTANT:** The current server listens on `127.0.0.1` for security, so external Android devices CANNOT POST to it. To accept events from a device, implement a protected authenticated ingestion endpoint and intentionally bind a LAN interface, or use `adb reverse tcp:5198 tcp:5198` for a USB-connected device and POST to `http://127.0.0.1:5198/api/traces` from Android. The latter is preferred for local debugging. Do not ship tracing code in release builds. Do not log passwords, access tokens, or personal data.

## Known limitations (prototype)

- OpenAPI 3 JSON is supported for route discovery; schema-derived parameter editors, OpenAPI auth flows, and OAuth token refresh are **not** implemented. Paste a Bearer token manually. Changing token can be done in Settings.
- No automatic Android/server correlation or DTO comparison until you instrument both apps. Trace tab is functional for DevBridge requests and manually submitted debug events.
- Package filter relies on `adb shell pidof`; processes that restart can have a few seconds of missed logs. Logs are capped in UI at 1,500 entries.
- The current UI Pause button freezes the visible window only approximately; it does not suspend the stream. A complete pause/resume buffer is future work.
- No standalone MSI installer yet; use published self-contained executable.
- The backend was not compiled in the authoring environment (no .NET SDK installed); build on Windows is required to verify runtime behavior.
