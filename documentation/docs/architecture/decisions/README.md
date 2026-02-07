# Architecture Decision Records (ADRs)

This directory contains Architecture Decision Records for the Interview Assist project.

## What is an ADR?

An Architecture Decision Record captures an important architectural decision along with its context and consequences. We use the [Nygard template](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions) for simplicity.

## ADR Index

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [ADR-001](ADR-001-realtime-api-websocket.md) | Use OpenAI Realtime API via WebSocket | Accepted | 2026-01-23 |
| [ADR-002](ADR-002-audio-capture-naudio.md) | Use NAudio for Windows Audio Capture | Accepted | 2026-01-23 |
| [ADR-003](ADR-003-question-detection-llm.md) | Use LLM for Question Detection | Superseded by ADR-007 | 2026-01-23 |
| [ADR-004](ADR-004-pipeline-vs-realtime.md) | Pipeline vs Realtime Implementation | Accepted | 2026-01-24 |
| [ADR-005](ADR-005-streaming-transcription-modes.md) | Streaming Transcription Modes | Superseded | 2026-01-29 |
| [ADR-006](ADR-006-deepgram-intent-recognition.md) | Deepgram Intent Recognition via REST API | Evaluated — unsuitable for question detection | 2026-02-06 |
| [ADR-007](ADR-007-multi-strategy-intent-detection.md) | Multi-Strategy Intent Detection | Accepted | 2026-02-06 |
| [ADR-008](ADR-008-utterance-segmentation.md) | Utterance Segmentation from Streaming ASR | Accepted | 2026-02-06 |

## Status Definitions

- **Proposed** - Under discussion
- **Accepted** - Decision has been made and is in effect
- **Deprecated** - No longer relevant or superseded
- **Superseded** - Replaced by another ADR

## Creating a New ADR

1. Copy the template below
2. Name the file `ADR-XXX-short-title.md`
3. Fill in all sections
4. Add to the index above
5. Submit for review

### Template

```markdown
# ADR-XXX: [Title]

## Status
[Proposed | Accepted | Deprecated | Superseded by ADR-XXX]

## Context
[What is the issue? What forces are at play?]

## Decision
[What is the change being proposed/made?]

## Consequences
[What are the positive and negative results?]
```
