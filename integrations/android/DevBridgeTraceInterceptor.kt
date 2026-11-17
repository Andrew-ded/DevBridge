package com.example.devbridge

import okhttp3.Interceptor
import okhttp3.Response
import okhttp3.OkHttpClient
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import org.json.JSONObject
import org.json.JSONArray
import okio.Buffer

/** Debug builds only. Run: adb reverse tcp:5198 tcp:5198 */
class DevBridgeTraceInterceptor(
    private val endpoint: String = "http://127.0.0.1:5198/api/traces"
) : Interceptor {
    private val sender = OkHttpClient.Builder()
        .connectTimeout(400, TimeUnit.MILLISECONDS)
        .callTimeout(800, TimeUnit.MILLISECONDS)
        .build()
    private val executor = Executors.newSingleThreadExecutor { task ->
        Thread(task, "DevBridgeTrace").apply { isDaemon = true }
    }

    override fun intercept(chain: Interceptor.Chain): Response {
        val incoming = chain.request()
        val existing = incoming.header("traceparent")
        val traceId = existing?.split('-')?.getOrNull(1)
            ?.takeIf { it.matches(Regex("[0-9a-f]{32}")) }
            ?: UUID.randomUUID().toString().replace("-", "")
        val spanId = UUID.randomUUID().toString().replace("-", "").take(16)
        val request = incoming.newBuilder()
            .header("traceparent", "00-$traceId-$spanId-01")
            .header("X-Correlation-ID", traceId)
            .build()
        val start = System.nanoTime()
        emit(traceId, "Android", "${request.method} ${request.url.encodedPath} sent", "info", "android-request", request.body?.let { body ->
            if (body.isOneShot() || body.isDuplex() || (body.contentLength() > 16384)) null else try {
                if (body.contentType()?.subtype?.contains("json", true) != true) null else {
                    val buffer = Buffer(); body.writeTo(buffer); safeJson(buffer.readUtf8())
                }
            } catch (_: Exception) { null }
        })
        return try {
            val response = chain.proceed(request)
            val ms = (System.nanoTime() - start) / 1_000_000
            emit(traceId, "Android", "HTTP ${response.code} received (${ms}ms)",
                if (response.isSuccessful) "success" else "error", "android-response",
                if (response.body?.contentType()?.subtype?.contains("json", true) == true)
                    try { safeJson(response.peekBody(16384).string()) } catch (_: Exception) { null }
                else null)
            response
        } catch (e: Exception) {
            emit(traceId, "Android", "Network error: ${e.javaClass.simpleName}", "error")
            throw e
        }
    }

    fun dto(traceId: String, stage: String, safeDescription: String) =
        emit(traceId, "Android DTO", "$stage: $safeDescription", "info")

    private fun safeJson(raw: String): String? {
        if (raw.length > 16384) return null
        return try {
            fun redact(value: Any?): Any? = when (value) {
                is JSONObject -> JSONObject().apply { value.keys().forEach { key ->
                    put(key, if (listOf("password", "passwd", "token", "secret", "authorization", "apikey", "api_key", "cookie").any { key.contains(it, true) }) "[REDACTED]" else redact(value.get(key)))
                } }
                is JSONArray -> JSONArray().apply { for (i in 0 until value.length()) put(redact(value.get(i))) }
                else -> value
            }
            val obj = if (raw.trimStart().startsWith("[")) JSONArray(raw) else JSONObject(raw)
            redact(obj).toString()
        } catch (_: Exception) { null }
    }

    private fun emit(id: String, source: String, message: String, level: String, stage: String? = null, data: String? = null) {
        executor.execute {
            try {
                val payload = JSONObject()
                    .put("correlationId", id).put("source", source)
                    .put("message", message).put("level", level)
                    .put("stage", stage).put("data", data)
                val body = payload.toString().toRequestBody("application/json".toMediaType())
                val req = okhttp3.Request.Builder().url(endpoint).post(body).build()
                sender.newCall(req).execute().use { }
            } catch (_: Exception) { /* Never interfere with application networking */ }
        }
    }
}
