# Implementation Plan: Solution Review Recommendations

## Overview

This plan addresses all recommendations from the solution review, organized into 10 phases for incremental implementation and testing.

---

## Phase 1: Bug Fix - Silent Audio Conversion Failures

**Priority:** High | **Files:** 2

### Problem
`AudioResampler.cs` lines 35-37 catch exceptions silently and return empty arrays, making audio debugging impossible.

### Changes

**1. Create `AudioResamplerDiagnostics.cs`** (NEW)
```
Interview-assist-audio-windows/AudioResamplerDiagnostics.cs
```
- Static class with `LogConversionError()` method
- Counter metric for conversion failures
- Follows pattern of `InterviewAssistMetrics.cs`

**2. Modify `AudioResampler.cs`**
```
interview-assist-audio-windows/AudioResampler.cs:35-37
```
- Replace bare `catch { }` with logging call
- Add format/size context to error messages

---

## Phase 2: Extract Hard-coded Model Versions

**Priority:** Medium | **Files:** 6

### Problem
Models hard-coded in multiple locations:
- `gpt-4o-realtime-preview-2024-12-17` in `OpenAiRealtimeApi.cs:22`
- `whisper-1` in 5+ files

### Changes

**1. Create `ModelConstants.cs`** (NEW)
```
Interview-assist-library/Constants/ModelConstants.cs
```
```csharp
public static class ModelConstants
{
    public const string DefaultRealtimeModel = "gpt-4o-realtime-preview-2024-12-17";
    public const string DefaultTranscriptionModel = "whisper-1";
    public const string RealtimeApiBaseUrl = "wss://api.openai.com/v1/realtime";
}
```

**2. Modify `RealtimeApiOptions.cs`**
```
Interview-assist-library/Realtime/RealtimeApiOptions.cs
```
- Add `RealtimeModel` property with default from constants
- Add `TranscriptionModel` property with default from constants

**3. Modify `OpenAiRealtimeApi.cs`**
```
Interview-assist-library/Realtime/OpenAiRealtimeApi.cs:22
```
- Remove hard-coded `WS_URL` constant
- Build URL dynamically: `$"{ModelConstants.RealtimeApiBaseUrl}?model={_options.RealtimeModel}"`

**4. Update builder in `ServiceCollectionExtensions.cs`**
- Add `WithRealtimeModel()` and `WithTranscriptionModel()` methods

---

## Phase 3: Correlation IDs for Request Tracing

**Priority:** High | **Files:** 3

### Problem
No request-level correlation for debugging distributed operations.

### Changes

**1. Create `CorrelationContext.cs`** (NEW)
```
Interview-assist-library/Diagnostics/CorrelationContext.cs
```
- `AsyncLocal<string?>` storage for correlation ID
- `GetOrCreate()` method generating 8-char IDs
- `BeginScope()` returning `IDisposable` for scoped correlation

**2. Modify `OpenAiRealtimeApi.cs`**
- Generate correlation ID in `StartAsync()`
- Include `CorrelationId` in all structured log messages
- Add `CorrelationId` property to expose current ID

**3. Modify log statements throughout**
- Update ~20 log calls to include `{CorrelationId}` parameter

---

## Phase 4: Structured Logging with Serilog

**Priority:** High | **Files:** 4 + console apps

### Changes

**1. Update `Interview-assist-library.csproj`**
```xml
<PackageReference Include="Serilog" Version="4.0.0" />
<PackageReference Include="Serilog.Extensions.Logging" Version="8.0.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
<PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
```

**2. Create `LoggingExtensions.cs`** (NEW)
```
Interview-assist-library/Extensions/LoggingExtensions.cs
```
- `AddInterviewAssistLogging()` extension method
- Default console template with correlation ID
- Optional file sink configuration

**3. Update console apps' `Program.cs`**
- Bootstrap Serilog before DI container
- Configure appropriate sinks

---

## Phase 5: Health Check Infrastructure

**Priority:** High | **Files:** 4 new + 1 modified

### Changes

**1. Create health check interfaces and types**
```
Interview-assist-library/Health/IHealthCheck.cs
Interview-assist-library/Health/HealthCheckResult.cs
Interview-assist-library/Health/HealthStatus.cs (enum)
```

**2. Create `RealtimeApiHealthCheck.cs`** (NEW)
```
Interview-assist-library/Health/RealtimeApiHealthCheck.cs
```
- Check `IsConnected` property
- Report rate-limit and quota status
- Return structured `HealthCheckResult`

**3. Create `HealthCheckService.cs`** (NEW)
```
Interview-assist-library/Health/HealthCheckService.cs
```
- Aggregates multiple `IHealthCheck` implementations
- Returns overall health status

**4. Update `ServiceCollectionExtensions.cs`**
- Add `AddInterviewAssistHealthChecks()` method

---

## Phase 6: Timeout Configurations

**Priority:** High | **Files:** 2

### Changes

**1. Modify `RealtimeApiOptions.cs`**
```csharp
public int WebSocketConnectTimeoutMs { get; init; } = 30000;
public int WebSocketKeepAliveIntervalMs { get; init; } = 30000;
public int HttpRequestTimeoutMs { get; init; } = 60000;
```

**2. Modify `OpenAiRealtimeApi.cs`**
- Apply `KeepAliveInterval` to WebSocket options
- Create linked `CancellationTokenSource` with timeout for connect
- Log timeout values on connection attempt

**3. Update builder in `ServiceCollectionExtensions.cs`**
- Add `WithWebSocketConnectTimeout()`, `WithHttpRequestTimeout()` methods

---

## Phase 7: Polly Integration for Resilience

**Priority:** Medium | **Files:** 3

### Problem
Polly 8.2.1 is referenced but unused.

### Changes

**1. Create `PollyPolicies.cs`** (NEW)
```
Interview-assist-library/Resilience/PollyPolicies.cs
```
- `GetTransientRetryPolicy()` - exponential backoff for HTTP 5xx/429
- `GetCircuitBreakerPolicy()` - break after N failures
- Logging integration via `ILogger`

**2. Modify HTTP-calling services**
```
Interview-assist-library/Transcription/OpenAiMicTranscriber.cs
Interview-assist-library/Pipeline/OpenAiChatCompletionService.cs
Interview-assist-library/Pipeline/OpenAiQuestionDetectionService.cs
```
- Wrap `HttpClient` calls with Polly policies
- Use `Policy.WrapAsync()` for retry + circuit breaker

---

## Phase 8: Backpressure Improvements

**Priority:** Medium | **Files:** 2

### Changes

**1. Modify `OpenAiRealtimeApi.cs`**
- Add `OnBackpressure` event: `event Action<int>?`
- Emit warning when queue depth reaches threshold (capacity - 2)
- Log with structured data: queue depth, chunk size

**2. Modify `InterviewAssistMetrics.cs`**
- Add `interview_assist.audio.backpressure_warnings` counter
- Add `interview_assist.audio.queue_depth` gauge (current depth)

---

## Phase 9: Graceful Shutdown Enhancements

**Priority:** Medium | **Files:** 2

### Changes

**1. Create `SessionState.cs`** (NEW)
```
Interview-assist-library/Realtime/SessionState.cs
```
```csharp
public record SessionState(
    string CorrelationId,
    IReadOnlyList<string> RecentTranscripts,
    DateTime SessionStart,
    DateTime SessionEnd);
```

**2. Modify `RealtimeApiOptions.cs`**
```csharp
public Func<SessionState, Task>? OnShutdownSaveState { get; init; }
```

**3. Modify `OpenAiRealtimeApi.cs`**
- Track `_sessionStartTime` on connect
- In `CleanupConnectionAsync()`, invoke `OnShutdownSaveState` if configured
- Wrap in try-catch to prevent shutdown failures

---

## Phase 10: Low Priority Optimizations

**Priority:** Low | **Files:** 3

### 10.1 Memory Pooling for Audio Buffers

**Modify `AudioResampler.cs`**
- Use `ArrayPool<short>.Shared` for intermediate buffers
- Rent before processing, return in finally block

### 10.2 Prometheus Metrics Export

**Create `PrometheusMetricsExporter.cs`** (NEW)
```
Interview-assist-library/Diagnostics/PrometheusMetricsExporter.cs
```
- `MeterListener` to collect metrics
- `GetPrometheusMetrics()` returning text format
- Optional: Add OpenTelemetry.Exporter.Prometheus package

### 10.3 Rate Limiting Documentation

**Create `RATE_LIMITING.md`** (NEW)
```
docs/RATE_LIMITING.md
```
- Document `HandleRateLimit()` behavior
- Document `RateLimitCircuitBreaker` states
- Document recovery flow and configuration options

---

## New Files Summary

| File | Phase | Purpose |
|------|-------|---------|
| `Constants/ModelConstants.cs` | 2 | Centralized model version constants |
| `Diagnostics/CorrelationContext.cs` | 3 | Async-local correlation ID storage |
| `Diagnostics/AudioResamplerDiagnostics.cs` | 1 | Audio conversion error logging |
| `Extensions/LoggingExtensions.cs` | 4 | Serilog configuration helpers |
| `Health/IHealthCheck.cs` | 5 | Health check interface |
| `Health/HealthCheckResult.cs` | 5 | Health check result record |
| `Health/RealtimeApiHealthCheck.cs` | 5 | API health check implementation |
| `Health/HealthCheckService.cs` | 5 | Health check aggregator |
| `Resilience/PollyPolicies.cs` | 7 | Polly retry/circuit breaker policies |
| `Realtime/SessionState.cs` | 9 | Shutdown state persistence record |
| `Diagnostics/PrometheusMetricsExporter.cs` | 10 | Metrics export (optional) |
| `docs/RATE_LIMITING.md` | 10 | Rate limiting documentation |

---

## Modified Files Summary

| File | Phases | Changes |
|------|--------|---------|
| `RealtimeApiOptions.cs` | 2,6,9 | Model, timeout, shutdown options |
| `OpenAiRealtimeApi.cs` | 2,3,6,8,9 | URL building, correlation, timeouts, backpressure |
| `AudioResampler.cs` | 1,10 | Error logging, memory pooling |
| `InterviewAssistMetrics.cs` | 8 | Backpressure metrics |
| `ServiceCollectionExtensions.cs` | 2,4,5,6 | Builder methods, logging, health |
| `Interview-assist-library.csproj` | 4 | Serilog packages |

---

## Package Additions

```xml
<!-- Interview-assist-library.csproj -->
<PackageReference Include="Serilog" Version="4.0.0" />
<PackageReference Include="Serilog.Extensions.Logging" Version="8.0.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
<PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
```

---

## Verification

After each phase:
1. Run `dotnet build interview-assist.sln` - expect 0 errors
2. Run `dotnet test Interview-assist-library-unit-tests` - expect all pass
3. Test console apps manually for runtime verification

Final verification:
1. All 100+ unit tests pass
2. Console apps start and connect successfully
3. Logs show correlation IDs and structured properties
4. Health checks return valid status
5. Audio conversion errors are logged (not silent)
