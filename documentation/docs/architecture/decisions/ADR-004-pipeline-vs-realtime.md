# ADR-004: Pipeline vs Realtime API Implementation

## Status

Accepted

## Context

The application provides two implementations of the `IRealtimeApi` interface for real-time interview assistance:

1. **OpenAiRealtimeApi** - Uses OpenAI's native Realtime API via WebSocket
2. **PipelineRealtimeApi** - Uses separate STT (Whisper) + semantic question detection + Chat API

Both approaches have distinct trade-offs that affect latency, cost, flexibility, and user experience.

### Requirements Driving This Decision

- Support for different interview scenarios (fast-paced vs. deliberate)
- Cost optimization for different usage patterns
- Flexibility in question detection strategies
- Ability to handle overlapping speech and rapid-fire questions

## Decision

Maintain both implementations behind the `IRealtimeApi` abstraction, with clear guidance on when to use each:

### Use OpenAiRealtimeApi (Realtime) When:

- **Low latency is critical** - Need sub-500ms response times
- **Turn-based conversation** - Speaker takes turns, clear pauses between statements
- **Server-side VAD is preferred** - Let OpenAI detect speech boundaries
- **Integrated audio+AI response** - Want audio output from the assistant
- **Simpler deployment** - Single API connection to manage

### Use PipelineRealtimeApi (Pipeline) When:

- **Semantic question detection** - Detect questions by meaning, not just silence
- **Overlapping speech** - Multiple speakers or rapid-fire questions
- **Queue-based processing** - Process multiple questions without blocking
- **Cost optimization** - Whisper + Chat API can be cheaper for some workloads
- **Custom detection logic** - Need fine-grained control over what triggers responses
- **Text-only output** - Don't need audio responses

## Implementation Details

### OpenAiRealtimeApi

```
Audio → WebSocket → OpenAI Realtime API → Transcript + Response
                         ↑
                   Server-side VAD
```

- Single WebSocket connection
- Audio streamed as Base64 PCM chunks
- VAD triggers automatic response generation
- Supports function calling for structured output

### PipelineRealtimeApi

```
Audio → Whisper STT → TranscriptBuffer → QuestionDetector → QuestionQueue
                                               ↓
                                    Chat API → Response
```

- Separate services for each stage
- Configurable detection interval and confidence threshold
- Deduplication prevents repeated question processing
- Non-blocking queue allows parallel processing

## Consequences

### Positive

- **Flexibility**: Users can choose the best approach for their use case
- **Abstraction**: `IRealtimeApi` interface allows transparent switching
- **Evolution**: Each implementation can evolve independently
- **Testing**: Pipeline approach is easier to unit test (mockable HTTP)

### Negative

- **Maintenance burden**: Two implementations to maintain
- **Complexity**: Users must understand trade-offs to choose correctly
- **Feature parity**: Some features may exist in one but not the other
- **Documentation**: Must clearly document when to use each

## Configuration

### Realtime API

```csharp
var options = new RealtimeApiOptionsBuilder()
    .WithApiKey(apiKey)
    .WithVadThreshold(0.5)
    .WithSilenceDurationMs(500)
    .Build();

var api = new OpenAiRealtimeApi(audioService, options);
```

### Pipeline API

```csharp
var options = new PipelineApiOptionsBuilder()
    .WithApiKey(apiKey)
    .WithDetectionIntervalMs(2000)
    .WithDetectionConfidenceThreshold(0.7)
    .Build();

var api = new PipelineRealtimeApi(audioService, options);
```
