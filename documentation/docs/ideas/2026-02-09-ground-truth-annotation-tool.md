# Ground Truth Annotation Tool

**Date:** 2026-02-09
**Status:** Idea

## Problem

The evaluation framework relies solely on GPT-4o for ground truth extraction (`GroundTruthExtractor`). There is no human validation step, so evaluation metrics are only as reliable as the LLM's extraction. Research (ACL 2025) shows that when annotators are shown LLM suggestions, overlap between human labels and LLM suggestions jumps from 40% baseline to 81-87% -- anchoring bias is real and must be actively mitigated in the UI design.

## Proposed Solution

A Terminal.Gui-based review tool launched via CLI:

```bash
dotnet run -- --review evaluations/session-2026-02-08-evaluation.json
```

The tool loads an existing evaluation JSON (which already contains `GroundTruth[]`, `FullTranscript`, `DetectedQuestions[]`, `Matches[]`) and presents each LLM-extracted question for human review. On completion, it saves validated ground truth that can be used for re-evaluation with corrected metrics.

---

## UI Concepts

### Concept A: Sequential Review (Prodigy-style)

Present one item at a time. The reviewer makes a single decision per screen, then auto-advances. This is the approach used by Prodigy ("Tinder for data") and is optimal for keyboard-driven speed.

```
+----------------------------------------------------------------------+
| Review Ground Truth: session-2026-02-08-142342.jsonl                 |
| Item 3 of 23  |  Accepted: 1  Rejected: 1  Modified: 0  Remaining: 20|
+----------------------------------------------------------------------+
| TRANSCRIPT CONTEXT                                                    |
|                                                                       |
| [00:42] Speaker 1: The Grammys last night were something else.       |
| [00:45] Speaker 1: But the question everyone is asking:              |
| [00:48] Speaker 1: >>> Is Billie Eilish about to lose her house? <<< |
| [00:52] Speaker 2: That's the big question on social media.          |
| [01:01] Speaker 1: She made those comments about the inauguration... |
|                                                                       |
+----------------------------------------------------------------------+
| EXTRACTED QUESTION                                                    |
|                                                                       |
|  Text:       "Is Billie Eilish about to lose her house?"             |
|  Subtype:    Rhetorical          Confidence: 0.95                    |
|                                                                       |
+----------------------------------------------------------------------+
| [A]ccept  [X]Reject  [E]dit  [S]ubtype  [N]ext  [P]rev  [U]ndo     |
+----------------------------------------------------------------------+
```

**Layout:** Three panels using `FrameView` (consistent with the existing `TranscriptionApp`):

1. **Header bar** -- session file, progress counter, running totals
2. **Transcript context** (top 60%) -- scrollable `TextView`, shows 3-5 utterances before and 2-3 after the current item, with the relevant text highlighted using a distinct `ColorScheme` (yellow-on-black, matching the existing intent colour convention)
3. **Item panel** (middle 25%) -- the extracted question's text, subtype, and confidence
4. **StatusBar** (bottom) -- key legend, always visible

**Advantages:**
- Forces engagement with every item (no skipping by accident)
- Fastest throughput for binary decisions (1-3 seconds per item)
- Simple to implement -- just TextViews and a state machine

**Disadvantages:**
- No overview of the full dataset
- Hard to spot patterns across multiple items

---

### Concept B: Split List + Detail View

Show all items in a list on the left, with the detail/context view on the right. The reviewer navigates the list and makes decisions on the selected item. Similar to an email client layout.

```
+---------------------------+------------------------------------------+
| QUESTIONS (23)            | TRANSCRIPT CONTEXT                       |
|                           |                                          |
|  [+] What is dependency   | [00:42] Speaker 1: ...the question       |
|      injection?           | everyone is asking:                      |
|  [+] How do you implement | [00:48] >>> Is Billie Eilish about to    |
|      the repository...    |     lose her house? <<<                  |
|> [ ] Is Billie Eilish     | [00:52] Speaker 2: That's the big        |
|      about to lose...     |     question on social media...          |
|  [ ] Can you explain the  |                                          |
|      difference...        +------------------------------------------+
|  [ ] Why is my app        | ITEM DETAIL                              |
|      throwing a null...   |                                          |
|  ...                      |  Text:    "Is Billie Eilish about to     |
|                           |           lose her house?"               |
| Legend:                   |  Subtype: Rhetorical                     |
|  [+] Accepted             |  Conf:    0.95                           |
|  [-] Rejected             |                                          |
|  [~] Modified             |                                          |
|  [ ] Pending              |                                          |
+---------------------------+------------------------------------------+
| [A]ccept [X]Reject [E]dit [S]ubtype [M]issed [Tab]Focus  Ctrl+Q Quit|
+---------------------------+------------------------------------------+
```

**Layout:** Two-column split using `FrameView`:

1. **Left panel (30%)** -- `ListView` of all extracted questions with status indicators (`[+]` accepted, `[-]` rejected, `[~]` modified, `[ ]` pending). Arrow keys navigate. Shows truncated text.
2. **Top-right panel (40%)** -- Transcript context (scrollable `TextView`), auto-scrolls to the selected item's approximate position. Highlighted text.
3. **Bottom-right panel (30%)** -- Item detail showing full text, subtype, confidence.
4. **StatusBar** -- key legend.

`Tab` switches focus between the list and context panels. Action keys (`A`, `X`, `E`, `S`) apply to the currently selected list item regardless of focus.

**Advantages:**
- Full overview of progress -- see which items are decided and which remain
- Easy to jump between items non-sequentially
- Can spot patterns (e.g., many similar false extractions)
- More familiar layout for users coming from IDEs

**Disadvantages:**
- More complex to implement (ListView + selection sync + multi-panel coordination)
- Risk of rushing through items without reading context (since you can see the queue)

---

### Concept C: Transcript-Centric Review

Display the full transcript as the primary view, with LLM-extracted questions highlighted inline. The reviewer moves through highlights and decides on each.

```
+----------------------------------------------------------------------+
| TRANSCRIPT (scroll with arrows, Tab to next highlight)               |
|                                                                       |
| [00:30] Speaker 1: So I've been working with ASP.NET Core lately     |
| and I'm really impressed with the middleware pipeline.                |
|                                                                       |
| [00:35] Speaker 2: Yeah it's quite elegant. ████████████████████████ |
| ██ What is dependency injection? ████████████████████████████████████ |
| That's something I keep hearing about.                                |
|                                                                       |
| [00:42] Speaker 1: Well dependency injection is a design pattern     |
| where you pass dependencies to a class rather than having the class  |
| create them itself.                                                   |
|                                                                       |
| [00:48] Speaker 2: ████████████████████████████████████████████████  |
| ██ How do you implement the repository pattern? █████████████████████|
| I've seen it in a few codebases but never really understood it.      |
|                                                                       |
+----------------------------------------------------------------------+
| CURRENT HIGHLIGHT (2 of 23)                                          |
|  Text:    "What is dependency injection?"                            |
|  Subtype: Definition [1-6 to change]    Confidence: 0.98            |
|  Status:  PENDING                                                    |
+----------------------------------------------------------------------+
| [A]ccept [X]Reject [E]dit [S]ubtype [Tab]Next [Shift+Tab]Prev [M]Add|
+----------------------------------------------------------------------+
```

**Layout:**

1. **Transcript panel (top 70%)** -- full transcript in a scrollable `TextView`. LLM-extracted questions are highlighted with a coloured background (using Terminal.Gui `ColorScheme`). `Tab` jumps to the next highlight. Each highlight's colour changes based on decision (green=accepted, red=rejected, yellow=pending).
2. **Detail panel (bottom 20%)** -- shows the currently focused item's full details.
3. **StatusBar** -- key legend.

**Advantages:**
- Best context -- the reviewer sees the question in its natural flow
- Intuitive for people who think in terms of "reading the transcript"
- Makes it easy to spot missed questions (they're visible in the transcript but not highlighted)
- Adding missed questions is natural -- just mark a region

**Disadvantages:**
- Most complex to implement (coloured spans within a TextView, managing highlight positions)
- Terminal.Gui 1.x has limited support for inline colour changes within a single TextView -- would likely need a custom view or use the `ColorScheme` switching approach
- Long transcripts could make navigation slow

---

### Concept D: Two-Phase Review

Combine sequential review with a summary/correction phase. Phase 1 is the Prodigy-style sequential pass. Phase 2 shows a summary table for final adjustments.

**Phase 1: Sequential Review** (same as Concept A)

Quick pass through all items. Accept/reject/modify. Optimise for speed.

**Phase 2: Summary Table**

```
+----------------------------------------------------------------------+
| REVIEW SUMMARY - Phase 2                                             |
+----------------------------------------------------------------------+
| #  | Decision | Text                              | Subtype    |Conf|
|----|----------|-----------------------------------|------------|-----|
|  1 | Accept   | What is dependency injection?     | Definition |0.98|
|  2 | Accept   | How do you implement the repo...  | HowTo      |0.92|
|  3 | REJECT   | I think we should refactor this   | -          |0.65|
|  4 | Modified | Can you explain async vs sync?    | Compare    |0.88|
|  5 | Accept   | Why is my app throwing a null...  | Troubleshoo|0.91|
|  + | ADDED    | What about thread safety?         | HowTo      |1.00|
+----------------------------------------------------------------------+
| STATISTICS                                                           |
|  Accepted: 3  Rejected: 1  Modified: 1  Added: 1                    |
|  LLM Agreement: 60%  Subtype Agreement: 80%                         |
+----------------------------------------------------------------------+
| [Enter]Edit selected  [D]elete  [M]Add missed  [F]Finalise  Ctrl+Q  |
+----------------------------------------------------------------------+
```

The summary table uses a `TableView` or `ListView`. The reviewer can:
- Re-open any item for editing
- Delete items they missed in Phase 1
- Add more missed questions
- See agreement statistics
- Finalise to save

**Advantages:**
- Best of both worlds: speed in Phase 1, oversight in Phase 2
- The summary phase catches mistakes from the quick pass
- Agreement statistics are immediately visible before finalising

**Disadvantages:**
- Two distinct UI modes to implement and maintain
- Slightly longer workflow

---

### Concept E: Transcript-Centric Annotation with Interactive Text Selection

Display the full transcript in the left panel with detected questions highlighted inline. The right panel maintains a synchronised list of all questions (both LLM-detected and human-added). The user can select text in the transcript to create new questions, and remove questions from the list. Both panels stay in bidirectional sync -- adding a question updates the transcript highlighting, and removing a question from the list removes its highlight.

This differs from Concept C in a key way: Concept C is **review-focused** (navigate through existing LLM highlights, accept/reject each). Concept E is **annotation-focused** (the transcript and question list are equal peers, and adding new questions by selecting text is a first-class operation, not a secondary action).

```
+--------------------------------------+-----------------------------------+
| TRANSCRIPT                           | QUESTIONS (5)                     |
|                                      |                                   |
| [00:30] Speaker 1: So I've been      |  1. [LLM] What is dependency      |
| working with ASP.NET Core lately     |     injection?                    |
| and I'm really impressed with the    |     Subtype: Definition           |
| middleware pipeline.                 |                                   |
|                                      |  2. [LLM] How do you implement    |
| [00:35] Speaker 2: Yeah it's quite   |     the repository pattern?       |
| elegant. ██████████████████████████  |     Subtype: HowTo                |
| █ What is dependency injection? ████ |                                   |
| █████████████████████████████████    |  3. [LLM] Is Billie Eilish about  |
| That's something I keep hearing      |     to lose her house?            |
| about.                               |     Subtype: Rhetorical           |
|                                      |                                   |
| [00:42] Speaker 1: Well dependency   |  4. [USR] What about thread       |
| injection is a design pattern        |     safety?                       |
| where you pass dependencies to a     |     Subtype: HowTo                |
| class rather than having the class   |                                   |
| create them itself.                  |  5. [USR] Can you walk me through |
|                                      |     the middleware pipeline?      |
| [00:48] Speaker 2: ████████████████  |     Subtype: HowTo                |
| █ How do you implement the repo ████ |                                   |
| █ pattern? █████████████████████████ |                                   |
| I've seen it in a few codebases      |                                   |
| but never really understood it.      |                                   |
|                                      |                                   |
| [00:55] Speaker 1: Sure. ████████   |                                   |
| █ Can you walk me through the ██████ |                                   |
| █ middleware pipeline? █████████████ |                                   |
| Let me show you...                   |                                   |
+--------------------------------------+-----------------------------------+
| [S]elect text  [D]elete selected  [T]ype subtype  [Tab]Focus  Ctrl+Q Quit|
+--------------------------------------+-----------------------------------+
```

**Layout:** Two-column split using `FrameView`:

1. **Left panel (60%)** -- full transcript in a scrollable `TextView`. LLM-detected questions are highlighted with a coloured background. User-added questions are highlighted in a distinct colour. Highlight colours stay in sync with the question list. The user can select a region of text (using Shift+Arrow or mouse) and press `S` to mark it as a question.
2. **Right panel (40%)** -- `ListView` of all questions, both LLM-detected (`[LLM]`) and user-added (`[USR]`). Each entry shows the question text and subtype. Arrow keys navigate; `D` removes the selected question (and removes its transcript highlight). `T` cycles the subtype.
3. **StatusBar** -- key legend, always visible.

**Bidirectional sync:**
- Selecting text in the transcript and pressing `S` adds a new `[USR]` entry to the right panel list and highlights the selected region.
- Pressing `D` on a list entry removes it from the list and removes the corresponding highlight from the transcript.
- Selecting an entry in the right panel scrolls the transcript to the corresponding position and briefly flashes the highlight.
- Selecting a highlight in the transcript selects the corresponding entry in the right panel.

**Mapping questions to transcript positions:**
- LLM-detected questions are mapped to transcript positions using `UtteranceId` from `IntentEventData` to find the corresponding `UtteranceEvent`, which provides `StartOffsetMs` and `EndOffsetMs`. Character offsets are calculated by summing segment text lengths from the reconstructed transcript.
- User-selected text uses the character offset directly from the `TextView` selection range.

**Advantages:**
- Most natural annotation workflow -- the user reads the transcript and highlights questions directly
- Adding missed questions is a first-class operation (text selection), not a secondary action
- Full dataset overview (question list) AND full context (transcript) visible simultaneously
- Bidirectional sync means the user never loses track of where a question appears in context
- Closest to how human annotators naturally work: read text, highlight interesting parts
- Reduces anchoring bias for missed questions -- the user is reading the full transcript, not just reviewing LLM suggestions

**Disadvantages:**
- Terminal.Gui 1.x has limited support for inline colour changes within a single `TextView` -- would likely need a custom view or extensive `ProcessKey` handling (same limitation as Concept C)
- Interactive text selection for highlighting requires custom `ProcessKey`/mouse event handling
- Bidirectional sync between two panels is complex to implement correctly
- Mapping LLM-detected questions back to character offsets in the transcript requires custom logic (no built-in method exists)
- Most complex concept to implement

---

## Recommended Approach

**Concept D (Two-Phase Review)** is the strongest option. It combines the speed of sequential review with the oversight of a summary table. However, it is also the most work to build.

For a pragmatic first implementation, **Concept B (Split List + Detail)** offers the best balance of capability and implementation complexity. It uses standard Terminal.Gui widgets (`ListView`, `FrameView`, `TextView`, `StatusBar`) that are consistent with the existing app, provides full overview and non-sequential navigation, and avoids the complexity of inline transcript highlighting.

---

## Keyboard Bindings

Designed for one-handed left-side operation (consistent with Prodigy conventions):

| Key | Action | Notes |
|-----|--------|-------|
| `A` | Accept item | Auto-advance in sequential mode |
| `X` | Reject item | Auto-advance in sequential mode |
| `E` | Edit text | Opens inline editor for the question text |
| `S` | Cycle subtype | Cycles: Definition > HowTo > Compare > Troubleshoot > Rhetorical > Clarification > (none) |
| `1`-`6` | Quick subtype | 1=Definition 2=HowTo 3=Compare 4=Troubleshoot 5=Rhetorical 6=Clarification |
| `N` / Right | Next item | |
| `P` / Left | Previous item | |
| `U` | Undo last decision | Reverts and navigates back |
| `M` | Add missed question | Opens text entry for a question the LLM missed |
| `Tab` | Switch panel focus | Between list, transcript, and detail panels |
| `Ctrl+S` | Save session | Also auto-saves after every decision |
| `Ctrl+Q` | Quit (with save prompt) | Consistent with existing app |

**Speed principle:** No confirmation dialogs for accept/reject. Undo (`U`) is the safety net. Visual feedback (colour change on the list item) replaces popups.

---

## Anchoring Bias Mitigations

Research shows LLM suggestions heavily bias human annotators. The UI should actively counteract this:

1. **No auto-accept.** Every item requires an explicit keypress, even high-confidence ones.
2. **Show confidence prominently.** Low-confidence items (< 0.7) should be visually flagged (e.g., orange/red text) to signal they deserve extra scrutiny.
3. **Show transcript context.** Force the reviewer to see the source material, not just the extracted text. The context panel is not optional -- it is always visible.
4. **Track agreement metrics.** Compare the validated ground truth against the original LLM output. Display the agreement rate in the summary phase so the reviewer can see how much correction was needed.
5. **Consider a "blind subtype" mode.** An optional mode where the LLM's subtype is hidden until the reviewer selects their own. Toggled via a config flag. This produces less biased subtype annotations at the cost of slower review.

---

## Session Persistence

Auto-save after every decision. The review session file is stored alongside the evaluation:

```
evaluations/
  session-2026-02-08-evaluation.json          # Original evaluation
  session-2026-02-08-evaluation-review.json    # Review session state
  session-2026-02-08-validated-ground-truth.json  # Final output
```

**Session file format:**

```json
{
  "sourceEvaluation": "session-2026-02-08-evaluation.json",
  "annotatorId": "stephen",
  "startedAt": "2026-02-09T10:30:00Z",
  "lastUpdatedAt": "2026-02-09T10:45:00Z",
  "currentIndex": 3,
  "totalItems": 23,
  "decisions": [
    {
      "index": 0,
      "action": "Accept",
      "originalText": "What is dependency injection?",
      "originalSubtype": "Definition",
      "finalText": "What is dependency injection?",
      "finalSubtype": "Definition",
      "reviewTimeMs": 1200
    },
    {
      "index": 1,
      "action": "Reject",
      "originalText": "I think we should refactor this code.",
      "originalSubtype": null,
      "finalText": null,
      "finalSubtype": null,
      "reviewTimeMs": 800
    },
    {
      "index": 2,
      "action": "Modify",
      "originalText": "Can you explain async",
      "originalSubtype": "Definition",
      "finalText": "Can you explain the difference between async and sync?",
      "finalSubtype": "Compare",
      "reviewTimeMs": 8500
    }
  ],
  "addedItems": [
    {
      "text": "What about thread safety?",
      "subtype": "HowTo",
      "confidence": 1.0,
      "approximatePosition": 8500,
      "addedAt": "2026-02-09T10:42:00Z"
    }
  ]
}
```

**Resume behaviour:** When `--review evaluation.json` detects an existing review session file, offer to resume from `currentIndex` or start fresh.

**Review time tracking:** Each decision records `reviewTimeMs`. Items with unusually long review times may indicate ambiguity and could be flagged for a second pass.

---

## Validated Ground Truth Output

On finalisation, the tool produces a validated ground truth file that can be fed back into the evaluation pipeline:

```json
{
  "generatedAt": "2026-02-09T10:50:00Z",
  "sourceEvaluation": "session-2026-02-08-evaluation.json",
  "annotatorId": "stephen",
  "questions": [
    {
      "text": "What is dependency injection?",
      "subtype": "Definition",
      "confidence": 0.98,
      "approximatePosition": 1234,
      "source": "llm-accepted"
    },
    {
      "text": "Can you explain the difference between async and sync?",
      "subtype": "Compare",
      "confidence": 0.88,
      "approximatePosition": 2100,
      "source": "llm-modified"
    },
    {
      "text": "What about thread safety?",
      "subtype": "HowTo",
      "confidence": 1.0,
      "approximatePosition": 8500,
      "source": "human-added"
    }
  ],
  "reviewStatistics": {
    "totalLlmItems": 23,
    "accepted": 18,
    "rejected": 3,
    "modified": 2,
    "added": 1,
    "llmAgreementRate": 0.78,
    "subtypeAgreementRate": 0.85,
    "averageReviewTimeMs": 2800,
    "totalReviewTimeMs": 64400
  }
}
```

This file uses the same `ExtractedQuestion` shape as the existing ground truth, with an added `source` field to track provenance. The evaluation pipeline can consume it directly via `--ground-truth validated-ground-truth.json` instead of re-extracting from the LLM.

---

## Colour Conventions

Consistent with the existing app's colour scheme (dark background, 16-colour mode):

| Element | Colour | Meaning |
|---------|--------|---------|
| Pending item | White on black | Not yet reviewed |
| Accepted item | Green on black | Confirmed correct |
| Rejected item | Red on black | Not a real question |
| Modified item | Cyan on black | Edited text or subtype |
| Added item | BrightYellow on black | Human-added (matches existing intent colour) |
| Highlighted transcript span | Yellow on DarkGray | The question's location in context |
| Low confidence warning | BrightRed | Confidence < 0.7 |
