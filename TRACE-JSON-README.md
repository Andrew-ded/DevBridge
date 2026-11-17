# DevBridge v0.5 — JSON Trace

Replace both integration files in the ASP.NET and Android projects, rebuild both debug applications and DevBridge. The original `Program.cs` registration and `adb reverse tcp:5198 tcp:5198` remain unchanged.

Trace displays causal stages Android request → Server request → Server response → Android response, without changing original event timestamps. Two side-by-side panels show Android and Server JSON, each with request and response sections.

Only JSON bodies up to 16 KiB are captured. Known secret-like keys are redacted recursively. Do not use with sensitive real production data: other PII keys may remain. Server middleware buffers a bounded response copy and can add latency. Android skips one-shot/duplex request bodies and uses peekBody on response. This is DEBUG ONLY.

No guarantee of cross-device clock synchronisation. Data capture does not imply Retrofit DTO mapping instrumentation.
