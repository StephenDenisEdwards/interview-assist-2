# API Reference

Public interfaces and key classes in the Interview Assist solution.

## Core Interfaces

### IRealtimeApi

**Namespace:** `InterviewAssist.Library.Realtime`
**File:** `Interview-assist-library/IRealtimeApi.cs`

Main abstraction for real-time API interaction (OpenAI Realtime API via WebSocket).

```csharp
public interface IRealtimeApi : IAsyncDisposable
{
    // Lifecycle
    event Action? OnConnected;
    event Action? OnReady;
    event Action? OnDisconnected;
    event Action<int, int>? OnReconnecting;  // attempt, maxAttempts

    // Diagnostics
    event Action<string>? OnInfo;
    event Action<string>? OnWarning;
    event Action<string>? OnDebug;
    event Action<Exception>? OnError;

    // Content
    event Action<string>? OnUserTranscript;
    event Action? OnSpeechStarted;
    event Action? OnSpeechStopped;
    event Action<string>? OnAssistantTextDelta;
    event Action? OnAssistantTextDone;
    event Action<string>? OnAssistantAudioTranscriptDelta;
    event Action? OnAssistantAudioTranscriptDone;
    event Action<string, string, string>? OnFunctionCallResponse;

    // Control
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    Task SendTextAsync(string text, bool requestResponse = true, bool interrupt = false);
    bool IsConnected { get; }
}
```

**Implementations:** `OpenAiRealtimeApi`, `PipelineRealtimeApi`

### IRealtimeSink

**Namespace:** `InterviewAssist.Library.Realtime`
**File:** `Interview-assist-library/Realtime/IRealtimeSink.cs`

Observer pattern for consuming `IRealtimeApi` events. Use the `WireToApi()` extension to subscribe.

```csharp
public interface IRealtimeSink : IDisposable
{
    void OnConnected();
    void OnReady();
    void OnDisconnected();
    void OnReconnecting(int attempt, int maxAttempts);
    void OnInfo(string message);
    void OnWarning(string message);
    void OnDebug(string message);
    void OnError(Exception ex);
    void OnUserTranscript(string text);
    void OnAssistantTextDelta(string delta);
    void OnAssistantTextDone();
    void OnFunctionCallResponse(string functionName, string answer, string code);
}

// Extension method
public static IDisposable WireToApi(this IRealtimeSink sink, IRealtimeApi api);
```

**Implementations:** `MauiRealtimeSink`, `ConsoleRealtimeSink`

### IAudioCaptureService

**Namespace:** `InterviewAssist.Library.Audio`
**File:** `Interview-assist-library/Audio/IAudioCaptureService.cs`

Platform-specific audio input abstraction.

```csharp
public interface IAudioCaptureService : IDisposable
{
    event Action<byte[]>? OnAudioChunk;
    void SetSource(AudioInputSource source);  // Microphone or Loopback
    void Start();
    void Stop();
    AudioInputSource GetSource();
}
```

**Implementations:** `WindowsAudioCaptureService` (NAudio, supports Microphone and Loopback)

---

## Intent Detection Pipeline

### IIntentDetectionStrategy

**Namespace:** `InterviewAssist.Library.Pipeline.Detection`
**File:** `Interview-assist-library/Pipeline/Detection/IIntentDetectionStrategy.cs`

Strategy interface for pluggable intent detection modes. See [ADR-007](../architecture/decisions/ADR-007-multi-strategy-intent-detection.md).

```csharp
public interface IIntentDetectionStrategy : IDisposable
{
    string ModeName { get; }
    Task ProcessUtteranceAsync(UtteranceEvent utterance, CancellationToken ct = default);
    void SignalPause();
    event Action<IntentEvent>? OnIntentDetected;
    event Action<IntentCorrectionEvent>? OnIntentCorrected;
}
```

**Implementations:** `HeuristicIntentStrategy`, `LlmIntentStrategy`, `ParallelIntentStrategy`

### ILlmIntentDetector

**Namespace:** `InterviewAssist.Library.Pipeline.Detection`
**File:** `Interview-assist-library/Pipeline/Detection/ILlmIntentDetector.cs`

Backend for LLM-based intent classification. Consumed by `LlmIntentStrategy`.

```csharp
public interface ILlmIntentDetector : IDisposable
{
    Task<IReadOnlyList<DetectedIntent>> DetectIntentsAsync(
        string text,
        string? previousContext = null,
        CancellationToken ct = default);
}
```

**Implementations:** `OpenAiIntentDetector`

### IQuestionDetectionService

**Namespace:** `InterviewAssist.Library.Pipeline`
**File:** `Interview-assist-library/Pipeline/IQuestionDetectionService.cs`

Legacy question detection interface (semantic question detection with pronoun resolution). Optional — if not registered, detection is disabled.

```csharp
public interface IQuestionDetectionService
{
    Task<IReadOnlyList<DetectedQuestion>> DetectQuestionsAsync(
        string transcriptText,
        string? previousContext = null,
        CancellationToken ct = default);
}
```

**Implementations:** `OpenAiQuestionDetectionService`

> **Note:** This interface serves a different purpose than `ILlmIntentDetector`. `IQuestionDetectionService` classifies questions by speech act type (Question, Imperative, Clarification, FollowUp), while `ILlmIntentDetector` classifies by intent subtype (Definition, HowTo, Compare, Troubleshoot, etc.). The pipeline-based approach (`IIntentDetectionStrategy` + `ILlmIntentDetector`) is the current recommended path.

---

## Pipeline Orchestration

### UtteranceIntentPipeline

**Namespace:** `InterviewAssist.Library.Pipeline.Utterance`
**File:** `Interview-assist-library/Pipeline/Utterance/UtteranceIntentPipeline.cs`

Central orchestrator for the ASR-to-intent processing pipeline. See [Design Document](DESIGN-utterance-intent-pipeline.md).

```csharp
public class UtteranceIntentPipeline : IDisposable
{
    // Events (pipeline stages)
    event Action<AsrEvent>? OnAsrPartial;
    event Action<AsrEvent>? OnAsrFinal;
    event Action<UtteranceEvent>? OnUtteranceOpen;
    event Action<UtteranceEvent>? OnUtteranceUpdate;
    event Action<UtteranceEvent>? OnUtteranceFinal;
    event Action<IntentEvent>? OnIntentCandidate;
    event Action<IntentEvent>? OnIntentFinal;
    event Action<ActionEvent>? OnActionTriggered;
    event Action<IntentCorrectionEvent>? OnIntentCorrected;

    // Properties
    string DetectionModeName { get; }
    IUtteranceBuilder UtteranceBuilder { get; }
    IIntentDetector IntentDetector { get; }
    IActionRouter ActionRouter { get; }
    IIntentDetectionStrategy? DetectionStrategy { get; }

    // Methods
    void ProcessAsrEvent(AsrEvent evt);
    void SignalUtteranceEnd();
    void ForceClose();
    void RegisterActionHandler(IntentSubtype subtype, Action<DetectedIntent> handler);
}
```

**Event flow:** `AsrPartial/Final` -> `UtteranceOpen/Update/Final` -> `IntentCandidate/Final` -> `ActionTriggered`

---

## Recording & Playback

### SessionRecorder

**Namespace:** `InterviewAssist.Library.Pipeline.Recording`
**File:** `Interview-assist-library/Pipeline/Recording/SessionRecorder.cs`

Records pipeline events to JSONL format for later playback or analysis.

```csharp
public class SessionRecorder : IDisposable
{
    SessionRecorder(UtteranceIntentPipeline pipeline);

    bool IsRecording { get; }
    string? CurrentFilePath { get; }
    event Action<string>? OnInfo;

    void Start(string filePath, SessionConfig config);
    void Stop();
}
```

### SessionPlayer

**Namespace:** `InterviewAssist.Library.Pipeline.Recording`
**File:** `Interview-assist-library/Pipeline/Recording/SessionPlayer.cs`

Replays recorded sessions through a `UtteranceIntentPipeline` with timing simulation.

```csharp
public class SessionPlayer
{
    SessionPlayer();

    event Action<RecordedEvent>? OnEventPlayed;
    event Action<string>? OnInfo;
    event Action? OnPlaybackComplete;

    SessionConfig? SessionConfig { get; }
    bool IsPlaying { get; }
    bool IsPaused { get; }
    int TotalEvents { get; }
    int CurrentEventIndex { get; }

    Task LoadAsync(string filePath, CancellationToken ct = default);
    Task PlayAsync(UtteranceIntentPipeline pipeline, CancellationToken ct = default);
    void Pause();
    void Resume();
    void TogglePause();
    void Stop();
}
```

---

## Evaluation

### EvaluationRunner

**Namespace:** `InterviewAssist.TranscriptionDetectionConsole`
**File:** `Interview-assist-transcription-detection-console/EvaluationRunner.cs`

Evaluation, benchmarking, and regression testing for intent detection strategies.

```csharp
public class EvaluationRunner
{
    EvaluationRunner(EvaluationOptions options);

    // Run evaluation on a session file
    Task<int> RunAsync(string sessionFile, string? outputFile, CancellationToken ct = default);

    // Analyze errors in an existing evaluation report
    Task<int> AnalyzeErrorsAsync(string reportFile, CancellationToken ct = default);

    // Find optimal confidence threshold
    Task<int> TuneThresholdAsync(string sessionFile,
        OptimizationTarget target = OptimizationTarget.F1, CancellationToken ct = default);

    // Compare multiple detection strategies side-by-side
    Task<int> CompareStrategiesAsync(string sessionFile, string? outputFile,
        HeuristicDetectionOptions? heuristicOptions, LlmDetectionOptions? llmOptions,
        CancellationToken ct = default);

    // Run regression test against a baseline
    Task<int> RunRegressionTestAsync(string baselineFile, string sessionFile,
        CancellationToken ct = default);

    // Create a baseline from session data
    Task<int> CreateBaselineAsync(string sessionFile, string outputFile, string version,
        CancellationToken ct = default);
}
```

See [Evaluation Instructions](../instructions/EvaluationInstructions.md) for usage and workflows.
