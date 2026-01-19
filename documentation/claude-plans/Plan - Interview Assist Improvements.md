# Implementation Plan: Interview Assist Improvements

## Overview
Implement all suggested improvements from the evaluation report for the real-time interview assistance application.

---

## Phase 1: Unit Test Project (High Priority)

### New Project Structure
```
Interview-assist-library-unit-tests/
├── Interview-assist-library-unit-tests.csproj
├── Utilities/
│   └── JsonRepairUtilityTests.cs
├── Context/
│   ├── ContextLoaderTests.cs
│   └── DocumentTextLoaderTests.cs
└── Pipeline/
    ├── TranscriptBufferTests.cs
    └── QuestionQueueTests.cs
```

### Tasks
1. Create `Interview-assist-library-unit-tests.csproj` (xUnit, Moq, net8.0)
2. Add to solution file
3. Create test classes for:
   - `JsonRepairUtility` - JSON repair edge cases
   - `TranscriptBuffer` - windowing, pruning, thread safety
   - `QuestionQueue` - enqueue, deduplication, disposal
   - `ContextLoader` - chunking, overlap, truncation

---

## Phase 2: Extract System Instructions (Medium Priority)

### Files to Modify
- `Interview-assist-library/Realtime/RealtimeApiOptions.cs` - Add new properties
- `Interview-assist-library/Realtime/OpenAiRealtimeApi.cs` - Use loader

### New Files
- `Interview-assist-library/Utilities/SystemInstructionsLoader.cs`

### Changes
1. Add to `RealtimeApiOptions`:
   ```csharp
   public Func<string>? SystemInstructionsFactory { get; init; }
   public string? SystemInstructionsFilePath { get; init; }
   ```
2. Create `SystemInstructionsLoader.Load()` with priority: Factory > FilePath > Property > Default
3. Move `DefaultSystemInstructions` constant from `OpenAiRealtimeApi.cs:20-32` to loader

---

## Phase 3: Add Observability/Metrics (Medium Priority)

### New Files
- `Interview-assist-library/Diagnostics/InterviewAssistMetrics.cs`

### Metrics to Track
| Metric | Type | Location |
|--------|------|----------|
| `websocket.connections` | Counter | `ConnectAsync()` |
| `websocket.disconnections` | Counter | `CleanupConnectionAsync()` |
| `websocket.reconnections` | Counter | `AttemptReconnectAsync()` |
| `rate_limit.hits` | Counter | `HandleRateLimit()` |
| `quota.exhausted` | Counter | `HandleQuotaExhausted()` |
| `response.latency_ms` | Histogram | `HandleResponseDone()` |
| `audio.chunks_processed` | Counter | `AudioSendLoop()` |
| `audio.chunks_dropped` | Counter | Channel full warning |

---

## Phase 4: Extract Magic Numbers (Low Priority)

### New Files
- `Interview-assist-library/Constants/AudioConstants.cs`
- `Interview-assist-library/Constants/QueueConstants.cs`
- `Interview-assist-library/Constants/DocumentConstants.cs`

### Constants to Extract
| Source File | Line | Constant | New Location |
|-------------|------|----------|--------------|
| `OpenAiRealtimeApi.cs` | 43-47 | Audio sample rate, bytes, channels | `AudioConstants` |
| `OpenAiRealtimeApi.cs` | 281 | Channel capacity (8) | `QueueConstants` |
| `OpenAiRealtimeApi.cs` | 308 | Reconnect cap (30000ms) | `RealtimeApiOptions` |
| `QuestionQueue.cs` | 21 | Default queue size (5) | `QueueConstants` |
| `QuestionQueue.cs` | 51 | Dedup multiplier (10) | `QueueConstants` |
| `TranscriptBuffer.cs` | 17 | Max age (30s) | `QueueConstants` |
| `ContextLoader.cs` | 24+ | Chunk sizes | `DocumentConstants` |

---

## Phase 5: Optimize Audio Resampling (Low Priority)

### New Files
- `interview-assist-audio-windows/AudioResampler.cs`

### Changes
1. Extract common resampling algorithm from `WindowsAudioCaptureService.cs:79-145`
2. Create `AudioResampler.ResampleToMonoPcm16()` static method
3. Simplify `ConvertLoopbackBuffer()` to single call

---

## Phase 6: Add Circuit Breaker Pattern (Low Priority)

### New Dependencies
- `Polly` (v8.2.1) in `Interview-assist-library.csproj`

### New Files
- `Interview-assist-library/Resilience/RateLimitCircuitBreaker.cs`

### Changes
1. Replace Timer-based recovery in `OpenAiRealtimeApi.cs:1098-1109`
2. Use Polly `AsyncCircuitBreakerPolicy`
3. States: Closed → Open (on rate limit) → Half-Open → Closed/Open

---

## Phase 7: Document Parsing Error Handling (Low Priority)

### Files to Modify
- `Interview-assist-library/Context/DocumentTextLoader.cs`

### Changes
1. Add `ILogger?` parameter to `LoadAllText()`
2. Wrap `ReadDocx()` and `ReadPdf()` in try-catch
3. Return error message string on failure: `[Error loading filename: message]`
4. Log warnings for missing files, unsupported formats

---

## Phase 8: Add DI Extensions (Low Priority)

### New Files
- `Interview-assist-library/Extensions/ServiceCollectionExtensions.cs`

### Methods to Add
```csharp
AddInterviewAssistCore(this IServiceCollection, Action<RealtimeApiOptions>)
AddInterviewAssistPipeline(this IServiceCollection, Action<PipelineApiOptions>)
```

### New Dependencies
- `Microsoft.Extensions.Options` (v8.0.0)

---

## File Summary

### New Files (12)
| Path | Purpose |
|------|---------|
| `Interview-assist-library-unit-tests/Interview-assist-library-unit-tests.csproj` | Test project |
| `Interview-assist-library-unit-tests/Utilities/JsonRepairUtilityTests.cs` | JSON repair tests |
| `Interview-assist-library-unit-tests/Context/ContextLoaderTests.cs` | Context tests |
| `Interview-assist-library-unit-tests/Context/DocumentTextLoaderTests.cs` | Document tests |
| `Interview-assist-library-unit-tests/Pipeline/TranscriptBufferTests.cs` | Buffer tests |
| `Interview-assist-library-unit-tests/Pipeline/QuestionQueueTests.cs` | Queue tests |
| `Interview-assist-library/Utilities/SystemInstructionsLoader.cs` | Instruction loading |
| `Interview-assist-library/Diagnostics/InterviewAssistMetrics.cs` | Metrics |
| `Interview-assist-library/Constants/AudioConstants.cs` | Audio constants |
| `Interview-assist-library/Constants/QueueConstants.cs` | Queue constants |
| `Interview-assist-library/Constants/DocumentConstants.cs` | Document constants |
| `Interview-assist-library/Resilience/RateLimitCircuitBreaker.cs` | Circuit breaker |
| `Interview-assist-library/Extensions/ServiceCollectionExtensions.cs` | DI extensions |
| `interview-assist-audio-windows/AudioResampler.cs` | Resampling utility |

### Modified Files (7)
| Path | Changes |
|------|---------|
| `interview-assist.sln` | Add test project |
| `Interview-assist-library/Interview-assist-library.csproj` | Add Polly, Options packages |
| `Interview-assist-library/Realtime/RealtimeApiOptions.cs` | Add instruction properties |
| `Interview-assist-library/Realtime/OpenAiRealtimeApi.cs` | Use loader, metrics, circuit breaker, constants |
| `Interview-assist-library/Context/DocumentTextLoader.cs` | Add error handling |
| `Interview-assist-library/Pipeline/QuestionQueue.cs` | Use constants |
| `interview-assist-audio-windows/WindowsAudioCaptureService.cs` | Use AudioResampler |

---

## Verification

```bash
# Build entire solution
dotnet build interview-assist.sln

# Run all tests
dotnet test interview-assist.sln

# Run specific test class
dotnet test --filter "FullyQualifiedName~JsonRepairUtilityTests"
```

### Manual Verification
1. Run console app with default settings - should work unchanged
2. Configure custom instructions file - verify loaded
3. Trigger rate limit - verify circuit breaker opens
4. Load corrupted document - verify error logged, no crash
