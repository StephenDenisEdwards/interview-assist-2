# ADR-003: Use LLM for Question Detection

## Status

Accepted

## Context

The application needs to detect when interview questions are asked in the transcribed audio stream. This enables:
- Highlighting questions for the user
- Triggering AI-generated answer suggestions
- Filtering out non-question content (small talk, filler)

Options considered:

1. **Heuristic detection** - Regex patterns, question words, punctuation analysis
2. **Local ML model** - Sentence classification model running locally
3. **LLM-based detection** - Send transcript to GPT model for classification

Requirements:
- High accuracy for technical interview questions
- Handle imperatives ("Explain...", "Describe...")
- Make questions self-contained (resolve pronouns from context)
- Filter promotional/meta content from video tutorials
- Deduplicate similar questions

## Decision

Use LLM-based question detection via OpenAI's Chat Completions API.

Implementation (`LlmQuestionDetector`):
- Maintains a rolling buffer of recent transcript text (max 2500 chars)
- Calls GPT-4o-mini with a specialized system prompt
- Requests JSON response with detected questions, types, and confidence scores
- Filters by confidence threshold (default 0.7)
- Deduplicates using Jaccard similarity on significant words
- Rate-limits API calls (default 1 second interval)

The system prompt instructs the model to:
- Identify technical interview questions and imperatives
- Resolve pronouns to make questions self-contained
- Ignore promotional content, meta commentary, and filler
- Classify as Question, Imperative, Clarification, or Follow-up

## Consequences

### Positive

- **High accuracy**: LLMs understand context and nuance better than heuristics
- **Self-contained questions**: Model resolves "What is it?" to "What is an abstract class?"
- **Flexible classification**: Handles questions, imperatives, follow-ups
- **Noise filtering**: Effectively ignores "subscribe and like" type content
- **No training required**: Works out of the box with prompt engineering

### Negative

- **API cost**: Each detection call costs tokens (mitigated by rate limiting)
- **Latency**: ~200-500ms per detection call
- **Internet dependency**: Cannot work offline
- **Prompt sensitivity**: Changes to prompt can affect detection quality
- **Rate limiting**: Must throttle calls to avoid API limits and costs

### Configuration Options

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `model` | gpt-4o-mini | Balances cost and quality |
| `confidenceThreshold` | 0.7 | Minimum confidence to report |
| `detectionIntervalMs` | 1000 | Rate limit between API calls |

### Future Considerations

- Consider local model (e.g., fine-tuned BERT) for offline/cost-sensitive use
- Could implement hybrid: fast heuristic pre-filter + LLM verification
- Caching common questions could reduce API calls
