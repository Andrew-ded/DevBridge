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
        emit(traceId, "Android", "${request.method} ${request.url.encodedPath} sent", "info")
        return try {
            val response = chain.proceed(request)
            val ms = (System.nanoTime() - start) / 1_000_000
            emit(traceId, "Android", "HTTP ${response.code} received (${ms}ms)",
                if (response.isSuccessful) "success" else "error")
            response
        } catch (e: Exception) {
            emit(traceId, "Android", "Network error: ${e.javaClass.simpleName}", "error")
            throw e
        }
    }

    fun dto(traceId: String, stage: String, safeDescription: String) =
        emit(traceId, "Android DTO", "$stage: $safeDescription", "info")

    private fun emit(id: String, source: String, message: String, level: String) {
        executor.execute {
            try {
                val payload = JSONObject()
                    .put("correlationId", id).put("source", source)
                    .put("message", message).put("level", level)
                val body = payload.toString().toRequestBody("application/json".toMediaType())
                val req = okhttp3.Request.Builder().url(endpoint).post(body).build()
                sender.newCall(req).execute().use { }
            } catch (_: Exception) { /* Never interfere with application networking */ }
        }
    }
}
