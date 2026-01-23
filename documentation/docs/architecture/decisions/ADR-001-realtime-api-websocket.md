# ADR-001: Use OpenAI Realtime API via WebSocket

## Status

Accepted

## Context

The application needs to transcribe audio in real-time and provide AI-powered responses during technical interviews. Several approaches were considered:

1. **Batch transcription APIs** (Whisper API) - Upload audio files, receive transcription after processing
2. **Streaming HTTP APIs** - Server-sent events or chunked responses
3. **WebSocket-based Realtime API** - Bidirectional streaming with OpenAI's Realtime API

Key requirements:
- Low latency (<500ms) for real-time feedback
- Continuous audio streaming without manual chunking
- Integrated AI responses (not just transcription)
- Voice Activity Detection (VAD) handled server-side

## Decision

Use OpenAI's Realtime API via WebSocket connection.

The implementation:
- Establishes a persistent WebSocket connection to `wss://api.openai.com/v1/realtime`
- Streams audio as Base64-encoded PCM chunks via `input_audio_buffer.append` messages
- Receives transcriptions via `conversation.item.input_audio_transcription.completed` events
- Receives AI responses via `response.text.delta` events
- Leverages server-side VAD for automatic speech boundary detection

## Consequences

### Positive

- **Low latency**: Bidirectional streaming eliminates HTTP request/response overhead
- **Integrated experience**: Single API handles transcription + AI response generation
- **Server-side VAD**: No need to implement complex voice activity detection locally
- **Automatic reconnection**: WebSocket protocol supports reconnection strategies
- **Reduced complexity**: No need to manage audio chunking boundaries manually

### Negative

- **Vendor lock-in**: Tightly coupled to OpenAI's specific protocol
- **Connection management**: Must handle WebSocket lifecycle (connect, reconnect, heartbeat)
- **Cost**: Realtime API pricing is higher than batch transcription
- **Internet dependency**: No offline fallback possible
- **Protocol complexity**: Must correctly implement OpenAI's event-based protocol
