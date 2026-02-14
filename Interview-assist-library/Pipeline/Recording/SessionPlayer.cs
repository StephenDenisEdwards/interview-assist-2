using System.Text;
using System.Text.Json;
using InterviewAssist.Library.Pipeline.Utterance;
using InterviewAssist.Library.Utilities;

namespace InterviewAssist.Library.Pipeline.Recording;

/// <summary>
/// Plays back a recorded session, feeding events to the pipeline at recorded timing.
/// </summary>
public sealed class SessionPlayer
{
    private readonly List<RecordedEvent> _events = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _pauseLock = new();

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _delayCts; // cancelled on pause to interrupt current delay
    private TaskCompletionSource? _pauseTcs; // non-null while paused; completed on resume
    private bool _isPlaying;
    private bool _isPaused;

    public event Action<RecordedEvent>? OnEventPlayed;
    public event Action<string>? OnInfo;
    public event Action? OnPlaybackComplete;

    public SessionConfig? SessionConfig { get; private set; }
    public bool IsPlaying => _isPlaying;
    public bool IsPaused => _isPaused;
    public int TotalEvents => _events.Count;
    public int CurrentEventIndex { get; private set; }

    public SessionPlayer()
    {
        _jsonOptions = PipelineJsonOptions.CamelCase;
    }

    /// <summary>
    /// Load a recorded session from a JSONL file.
    /// </summary>
    public async Task LoadAsync(string filePath, CancellationToken ct = default)
    {
        _events.Clear();
        SessionConfig = null;
        CurrentEventIndex = 0;

        using var reader = new StreamReader(filePath, Encoding.UTF8);
        string? line;
        int lineNumber = 0;

        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var evt = JsonSerializer.Deserialize<RecordedEvent>(line, _jsonOptions);
                if (evt == null) continue;

                if (evt is RecordedSessionMetadata metadata)
                {
                    SessionConfig = metadata.Config;
                }

                _events.Add(evt);
            }
            catch (JsonException ex)
            {
                OnInfo?.Invoke($"Warning: Failed to parse line {lineNumber}: {ex.Message}");
            }
        }

        OnInfo?.Invoke($"Loaded {_events.Count} events from {Path.GetFileName(filePath)}");
    }

    /// <summary>
    /// Play back the loaded session, feeding ASR events to the pipeline at recorded timing.
    /// </summary>
    public async Task PlayAsync(UtteranceIntentPipeline pipeline, CancellationToken ct = default)
    {
        if (_events.Count == 0)
        {
            throw new InvalidOperationException("No events loaded. Call LoadAsync first.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _isPlaying = true;
        CurrentEventIndex = 0;
        var token = _cts.Token;

        try
        {
            long previousOffsetMs = 0;

            foreach (var evt in _events)
            {
                token.ThrowIfCancellationRequested();

                // Skip metadata during playback
                if (evt is RecordedSessionMetadata)
                {
                    CurrentEventIndex++;
                    continue;
                }

                // Block while paused (async-friendly)
                TaskCompletionSource? tcs;
                lock (_pauseLock) { tcs = _pauseTcs; }
                if (tcs != null)
                {
                    // Wait until resumed or cancelled
                    using var reg = token.Register(() => tcs.TrySetCanceled());
                    await tcs.Task;
                }

                token.ThrowIfCancellationRequested();

                // Wait for appropriate delay to simulate realtime
                var delayMs = evt.OffsetMs - previousOffsetMs;
                if (delayMs > 0)
                {
                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    lock (_pauseLock)
                    {
                        _delayCts = delayCts;
                        // Close race: if Pause() was called between the check above and here,
                        // cancel immediately so we don't sleep through the pause request
                        if (_pauseTcs != null) delayCts.Cancel();
                    }
                    try
                    {
                        await Task.Delay((int)delayMs, delayCts.Token);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        // Delay interrupted by pause — wait on the pause gate
                    }
                    finally
                    {
                        lock (_pauseLock) { _delayCts = null; }
                    }

                    // Re-check pause after delay (handles both race and interrupt cases)
                    TaskCompletionSource? tcs2;
                    lock (_pauseLock) { tcs2 = _pauseTcs; }
                    if (tcs2 != null)
                    {
                        using var reg = token.Register(() => tcs2.TrySetCanceled());
                        await tcs2.Task;
                    }
                    token.ThrowIfCancellationRequested();
                }
                previousOffsetMs = evt.OffsetMs;

                // Dispatch event to pipeline (only input events)
                DispatchEvent(evt, pipeline);
                OnEventPlayed?.Invoke(evt);
                CurrentEventIndex++;
            }

            OnInfo?.Invoke("Playback complete");
            OnPlaybackComplete?.Invoke();
        }
        catch (OperationCanceledException)
        {
            OnInfo?.Invoke("Playback cancelled");
        }
        finally
        {
            _isPlaying = false;
        }
    }

    private void DispatchEvent(RecordedEvent evt, UtteranceIntentPipeline pipeline)
    {
        switch (evt)
        {
            case RecordedAsrEvent asr:
                // Feed ASR event to pipeline - this is the input that drives intent detection
                pipeline.ProcessAsrEvent(new AsrEvent
                {
                    Text = asr.Data.Text,
                    IsFinal = asr.Data.IsFinal,
                    SpeakerId = asr.Data.SpeakerId,
                    IsUtteranceEnd = asr.Data.IsUtteranceEnd
                });
                break;

            case RecordedUtteranceEndSignal:
                // Signal utterance end to pipeline
                pipeline.SignalUtteranceEnd();
                break;

            // Note: UtteranceEvent, IntentEvent, ActionEvent are OUTPUT events
            // They are re-generated by the pipeline during playback
            // We record them for analysis comparison but don't replay them
        }
    }

    /// <summary>
    /// Pause playback. Interrupts the current delay for immediate effect.
    /// </summary>
    public void Pause()
    {
        if (!_isPlaying || _isPaused) return;

        lock (_pauseLock)
        {
            _isPaused = true;
            _pauseTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _delayCts?.Cancel(); // interrupt current delay so pause takes effect immediately
        }
        OnInfo?.Invoke("Playback paused");
    }

    /// <summary>
    /// Resume paused playback.
    /// </summary>
    public void Resume()
    {
        if (!_isPaused) return;

        lock (_pauseLock)
        {
            _isPaused = false;
            _pauseTcs?.TrySetResult();
            _pauseTcs = null;
        }
        OnInfo?.Invoke("Playback resumed");
    }

    /// <summary>
    /// Toggle between paused and playing.
    /// </summary>
    public void TogglePause()
    {
        if (_isPaused)
            Resume();
        else
            Pause();
    }

    /// <summary>
    /// Stop playback.
    /// </summary>
    public void Stop()
    {
        lock (_pauseLock)
        {
            _isPaused = false;
            _pauseTcs?.TrySetResult(); // unblock if paused so cancellation can proceed
            _pauseTcs = null;
        }
        _cts?.Cancel();
    }
}
