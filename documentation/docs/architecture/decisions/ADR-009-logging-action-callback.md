# ADR-009: Retain Action<string> Callback Logging Over ILogger

## Status

Accepted

## Context

The codebase uses `Action<string>` callbacks for diagnostic logging in both headless mode (console + file) and UI mode (Terminal.Gui debug panel). As the headless mode gained richer output (LLM requests, action events, all intent types), the `log` callback is threaded through more layers: `CreateDetectionStrategyForMode` → `CreateLlmStrategyStatic` → `WireDetectorRequestLogging`.

The question arose whether to replace this pattern with `Microsoft.Extensions.Logging.ILogger<T>`, which is the standard .NET logging abstraction.

### Forces

- **Two fundamentally different output modes**: Headless mode writes timestamped lines to console and a log file. UI mode routes messages to Terminal.Gui panels via `AddDebug()` on the main loop. These do not map cleanly to a single `ILogger` sink configuration.
- **No need for log levels**: The current output is all diagnostic — there is no filtering by severity. Everything written is intended to be visible.
- **No need for structured logging**: There are no correlation IDs, scopes, or machine-parseable fields. The output is human-readable console text.
- **Simplicity**: The `Action<string>` pattern is zero-dependency, trivially testable, and easy to wire at each call site.
- **Callback proliferation**: The `log` parameter is passed through several static factory methods, which adds noise to signatures.

## Decision

Keep the `Action<string>` callback pattern for diagnostic logging. Do not introduce `ILogger` or a logging framework.

## Consequences

### Positive

- No new dependencies — honours the project guideline "do not introduce new dependencies unless asked."
- The two output modes (Terminal.Gui panels vs headless console/file) remain cleanly separated without sink configuration complexity.
- Diagnostic output stays simple: one callback, one destination, no filtering.

### Negative

- The `log` callback parameter continues to thread through static factory methods. If more layers need logging, signatures get noisier.
- No log-level filtering — all output is either on or off per mode.

### Revisit Triggers

- Adding a third output sink (e.g. telemetry, remote logging).
- Needing log-level filtering (e.g. suppressing verbose LLM request bodies in production).
- The library layer (`Interview-assist-library`) itself needing to emit diagnostic logs, which would require passing callbacks across assembly boundaries.
