using System.Diagnostics.Metrics;

namespace InterviewAssist.Library.Diagnostics;

/// <summary>
/// Provides metrics for monitoring the Interview Assist application.
/// Uses System.Diagnostics.Metrics for compatibility with various monitoring systems.
/// </summary>
public static class InterviewAssistMetrics
{
    /// <summary>
    /// The meter name used for all Interview Assist metrics.
    /// </summary>
    public const string MeterName = "InterviewAssist.Library";

    private static readonly Meter s_meter = new(MeterName, "1.0.0");

    #region WebSocket Metrics
    private static readonly Counter<long> s_websocketConnections = s_meter.CreateCounter<long>(
        name: "interview_assist.websocket.connections",
        unit: "{connections}",
        description: "Number of WebSocket connections established");

    private static readonly Counter<long> s_websocketDisconnections = s_meter.CreateCounter<long>(
        name: "interview_assist.websocket.disconnections",
        unit: "{disconnections}",
        description: "Number of WebSocket disconnections");

    private static readonly Counter<long> s_websocketReconnections = s_meter.CreateCounter<long>(
        name: "interview_assist.websocket.reconnections",
        unit: "{reconnections}",
        description: "Number of WebSocket reconnection attempts");
    #endregion

    #region Rate Limiting Metrics
    private static readonly Counter<long> s_rateLimitHits = s_meter.CreateCounter<long>(
        name: "interview_assist.rate_limit.hits",
        unit: "{hits}",
        description: "Number of rate limit hits");

    private static readonly Counter<long> s_quotaExhausted = s_meter.CreateCounter<long>(
        name: "interview_assist.quota.exhausted",
        unit: "{events}",
        description: "Number of quota exhausted events");
    #endregion

    #region Response Metrics
    private static readonly Histogram<double> s_responseLatency = s_meter.CreateHistogram<double>(
        name: "interview_assist.response.latency",
        unit: "ms",
        description: "Response latency in milliseconds");
    #endregion

    #region Audio Metrics
    private static readonly Counter<long> s_audioChunksProcessed = s_meter.CreateCounter<long>(
        name: "interview_assist.audio.chunks_processed",
        unit: "{chunks}",
        description: "Number of audio chunks processed");

    private static readonly Counter<long> s_audioChunksDropped = s_meter.CreateCounter<long>(
        name: "interview_assist.audio.chunks_dropped",
        unit: "{chunks}",
        description: "Number of audio chunks dropped due to backpressure");

    private static readonly Counter<long> s_backpressureWarnings = s_meter.CreateCounter<long>(
        name: "interview_assist.audio.backpressure_warnings",
        unit: "{warnings}",
        description: "Number of backpressure warning events");

    private static int s_currentQueueDepth;
    #endregion

    #region Public Methods
    /// <summary>
    /// Records a WebSocket connection.
    /// </summary>
    public static void RecordConnection() => s_websocketConnections.Add(1);

    /// <summary>
    /// Records a WebSocket disconnection.
    /// </summary>
    public static void RecordDisconnection() => s_websocketDisconnections.Add(1);

    /// <summary>
    /// Records a WebSocket reconnection attempt.
    /// </summary>
    public static void RecordReconnection() => s_websocketReconnections.Add(1);

    /// <summary>
    /// Records a rate limit hit.
    /// </summary>
    public static void RecordRateLimitHit() => s_rateLimitHits.Add(1);

    /// <summary>
    /// Records a quota exhausted event.
    /// </summary>
    public static void RecordQuotaExhausted() => s_quotaExhausted.Add(1);

    /// <summary>
    /// Records response latency in milliseconds.
    /// </summary>
    /// <param name="latencyMs">The response latency in milliseconds.</param>
    public static void RecordResponseLatency(double latencyMs) => s_responseLatency.Record(latencyMs);

    /// <summary>
    /// Records an audio chunk being processed.
    /// </summary>
    public static void RecordAudioChunkProcessed() => s_audioChunksProcessed.Add(1);

    /// <summary>
    /// Records an audio chunk being dropped.
    /// </summary>
    public static void RecordAudioChunkDropped() => s_audioChunksDropped.Add(1);

    /// <summary>
    /// Records a backpressure warning event.
    /// </summary>
    public static void RecordBackpressureWarning() => s_backpressureWarnings.Add(1);

    /// <summary>
    /// Updates the current audio queue depth.
    /// </summary>
    /// <param name="depth">The current queue depth.</param>
    public static void SetQueueDepth(int depth) => Interlocked.Exchange(ref s_currentQueueDepth, depth);

    /// <summary>
    /// Gets the current audio queue depth.
    /// </summary>
    public static int CurrentQueueDepth => Volatile.Read(ref s_currentQueueDepth);
    #endregion
}
