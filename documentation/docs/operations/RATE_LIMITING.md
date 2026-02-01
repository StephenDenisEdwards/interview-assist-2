# Rate Limiting and Recovery

This document describes the rate limiting detection and recovery mechanisms in the Interview Assist application.

## Overview

The OpenAI Realtime API enforces rate limits to ensure fair usage. When these limits are exceeded, the application implements automatic detection and recovery mechanisms.

## Rate Limit Detection

Rate limits are detected through:

1. **HTTP 429 Status Code**: Direct rate limit response from the API
2. **Error Events**: Error events with `type: "rate_limit_error"`
3. **Transcription Failures**: Transcription failures containing "429" in the message
4. **Response Failures**: Response status "failed" with rate-limit indicators

## Circuit Breaker Pattern

The `RateLimitCircuitBreaker` class implements the circuit breaker pattern with three states:

### States

| State | Description | Behavior |
|-------|-------------|----------|
| **Closed** | Normal operation | Requests flow through normally |
| **Open** | Rate limited | Requests are blocked, audio capture paused |
| **HalfOpen** | Testing recovery | Single request allowed to test if limit lifted |

### State Transitions

```
         Rate Limit Hit
Closed ─────────────────────► Open
   ▲                            │
   │                            │ Recovery Timer
   │  Success                   ▼
   └────────────────────── HalfOpen
              ▲                 │
              │  Rate Limit     │
              └─────────────────┘
```

## Configuration Options

Configure rate limit behavior via `RealtimeApiOptions`:

```csharp
services.AddInterviewAssistCore(options => options
    .WithApiKey("your-api-key")
    .WithRateLimitRecovery(
        enabled: true,           // Enable automatic recovery
        recoveryDelayMs: 60000,  // Initial wait before retry (1 minute)
        maxDelayMs: 30000        // Maximum backoff delay (30 seconds)
    ));
```

| Option | Default | Description |
|--------|---------|-------------|
| `EnableRateLimitRecovery` | `true` | Whether to automatically attempt recovery |
| `RateLimitRecoveryDelayMs` | `60000` | Initial delay before recovery attempt |
| `MaxReconnectDelayMs` | `30000` | Maximum delay (caps exponential backoff) |

## Recovery Flow

1. **Detection**: Rate limit detected via API response
2. **Pause**: Audio capture is stopped to prevent further requests
3. **Notification**: `OnWarning` event fired with rate limit context
4. **Circuit Opens**: Circuit breaker transitions to Open state
5. **Wait**: Exponential backoff delay calculated
6. **Half-Open**: After delay, circuit enters HalfOpen state
7. **Test**: Audio capture resumes for test request
8. **Result**:
   - Success → Circuit closes, normal operation resumes
   - Failure → Circuit re-opens with increased delay

## Exponential Backoff

Delay calculation: `baseDelay * 2^(failures-1)`, capped at `maxDelay`

| Attempt | Delay (60s base) |
|---------|------------------|
| 1 | 60 seconds |
| 2 | 120 seconds |
| 3 | 240 seconds (capped at 30s max) |

## Events

Subscribe to these events for rate limit visibility:

```csharp
api.OnWarning += message => {
    if (message.Contains("Rate limit"))
        Console.WriteLine($"Rate limited: {message}");
};

api.OnInfo += message => {
    if (message.Contains("resume"))
        Console.WriteLine($"Recovery: {message}");
};
```

## Metrics

Monitor rate limiting with these metrics:

| Metric | Type | Description |
|--------|------|-------------|
| `interview_assist.rate_limit.hits` | Counter | Number of rate limit events |
| `interview_assist.quota.exhausted` | Counter | Quota exhausted events (fatal) |

## Quota Exhausted

When quota is completely exhausted (`insufficient_quota` error):

1. This is treated as a **fatal** error
2. Audio capture is permanently stopped
3. Session is cancelled
4. No automatic recovery is attempted
5. User must add credits or wait for quota reset

## Best Practices

1. **Monitor Warnings**: Subscribe to `OnWarning` for rate limit notifications
2. **Graceful Degradation**: Implement UI feedback when rate limited
3. **Avoid Bursts**: Spread requests over time when possible
4. **Check Quota**: Monitor `insufficient_quota` errors for billing issues
5. **Configure Delays**: Adjust recovery delays based on your usage patterns

## Polly Integration

For HTTP operations outside the WebSocket, use the provided Polly policies:

```csharp
var policy = PollyPolicies.GetResiliencePolicy(logger);
var response = await policy.ExecuteAsync(() => httpClient.GetAsync(url));
```

This wraps retry with circuit breaker for HTTP 429 and 5xx errors.
