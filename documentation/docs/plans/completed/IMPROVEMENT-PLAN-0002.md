# Improvement Plan 0002: Solution Review Remediation

**Created:** 2026-01-24
**Status:** In Progress
**Last Updated:** 2026-01-24

This plan addresses the improvements identified in the solution review.

---

## Completed

### 1. ~~Dependency Concerns in Core Library~~
- [x] Removed unused NAudio dependency from `Interview-assist-library.csproj`
- NAudio remains only in `interview-assist-audio-windows` where it's actually used

### 4. ~~SAD Discrepancy with Actual Projects~~
- [x] Updated Section 5.1 (Level 1: Solution Structure) to reflect actual projects
- [x] Updated mermaid diagram to show correct project structure
- [x] Removed references to non-existent MAUI desktop project
- [x] Updated technology stack table

### 6. ~~Magic Numbers in OpenAiRealtimeApi~~
- [x] Created `Constants/RealtimeConstants.cs` with:
  - `MaxRecentTranscripts = 50`
  - `WebSocketBufferSize = 64 * 1024`
  - `FunctionParseRetryDelayMs = 600`
- [x] Updated `OpenAiRealtimeApi.cs` to use constants

### 7. ~~Duplicate Event Dispatcher Pattern~~
- [x] Created `Realtime/EventDispatcher.cs` as shared implementation
- [x] Updated `OpenAiRealtimeApi` to use `EventDispatcher`
- [x] Updated `PipelineRealtimeApi` to use `EventDispatcher`
- [x] Removed ~90 lines of duplicated code

### 8. ~~Missing ADR for Pipeline Architecture~~
- [x] Created `ADR-004-pipeline-vs-realtime.md`
- [x] Added to solution file and SAD ADR table

### 9. ~~Potential Memory Inefficiency~~
- [x] Replaced `List<string>` with `Queue<string>` for `_recentTranscripts`
- [x] Updated `TrackRecentTranscript` to use `Enqueue`/`Dequeue` (O(1) vs O(n))

### 10. ~~JSON Serialization Inconsistency~~
- [x] Audited all JSON usage - only `System.Text.Json` is used in code
- [x] Removed unused `Newtonsoft.Json` package reference

### 11. ~~Add Root README.md~~
- [x] Created comprehensive README.md with:
  - Project overview
  - Prerequisites
  - Quick start guide
  - Project structure table
  - Two modes of operation explanation
  - Configuration documentation
  - Testing commands
  - Links to architecture docs

---

## Remaining Tasks

### 2. Custom PdfPig Package Version
**Priority:** Medium
**Effort:** Small
**Status:** Investigated - Decision Needed

**Findings:**
- Using `UglyToad.PdfPig Version="1.7.0-custom-5"` - a custom prerelease
- Stable version available: `PdfPig 0.1.13` ([NuGet](https://www.nuget.org/packages/PdfPig/))
- Current usage in `DocumentTextLoader.cs` is basic (just `PdfDocument.Open()` and `page.Text`)
- Custom package "Not found at sources" - potential restore issues for new developers

**Options:**
1. Switch to stable `PdfPig 0.1.13` - needs testing to ensure compatibility
2. Keep custom version and document source/build instructions
3. Inline the minimal PDF reading code needed (eliminate dependency)

**Recommendation:** Test with stable version. If compatible, switch. If not, create ADR documenting why custom version is needed.

---

### 3. Missing Test Coverage for Core Components
**Priority:** High
**Effort:** Large
**Status:** Deferred to future sprint

**Problem:** No unit tests for critical WebSocket/HTTP components.

**Required for full coverage:**
- [ ] Create `Realtime/OpenAiRealtimeApiTests.cs`
- [ ] Create `Pipeline/OpenAiQuestionDetectionServiceTests.cs`
- [ ] Create `Pipeline/OpenAiChatCompletionServiceTests.cs`

**Approach:**
1. Extract `ClientWebSocket` behind `IWebSocketClient` interface
2. Create mock implementations for testing

---

### 5. Inconsistent Naming Conventions
**Priority:** Low
**Effort:** Medium (breaking change)
**Status:** Deferred

**Recommendation:** Defer to future major version due to breaking nature.

---

## Summary

| Task | Status |
|------|--------|
| 1. Remove NAudio from core library | Done |
| 2. PdfPig custom version | Decision needed |
| 3. Add unit tests for core components | Deferred |
| 4. Update SAD | Done |
| 5. Naming conventions | Deferred |
| 6. Extract magic numbers | Done |
| 7. Extract EventDispatcher | Done |
| 8. Create ADR-004 | Done |
| 9. Fix Queue memory inefficiency | Done |
| 10. Remove Newtonsoft.Json | Done |
| 11. Add README.md | Done |

**Completed:** 9 of 11 tasks
**Remaining:** 2 tasks (1 needs decision, 1 deferred as large effort)

---

## Implementation Summary

### Phase 1 - Documentation
| Task | Files Changed |
|------|---------------|
| Update SAD | `documentation/docs/architecture/SAD.md` |
| Create ADR-004 | `documentation/docs/architecture/decisions/ADR-004-pipeline-vs-realtime.md` |
| Add README.md | `README.md` (new) |

### Phase 2 - Code Quality
| Task | Files Changed |
|------|---------------|
| Extract magic numbers | Created `Interview-assist-library/Constants/RealtimeConstants.cs` |
| Fix Queue inefficiency | `OpenAiRealtimeApi.cs` - changed `List<string>` to `Queue<string>` |
| Extract EventDispatcher | Created `Interview-assist-library/Realtime/EventDispatcher.cs`, updated both API classes |

### Phase 3 - Investigation
| Task | Outcome |
|------|---------|
| PdfPig custom version | Investigated - stable version 0.1.13 available, needs testing before switch |
| JSON library audit | Removed unused `Newtonsoft.Json` package |

### Results
- **Build:** Successful (0 errors)
- **Tests:** 144 passed

### New Files Created
- `README.md`
- `Interview-assist-library/Constants/RealtimeConstants.cs`
- `Interview-assist-library/Realtime/EventDispatcher.cs`
- `documentation/docs/architecture/decisions/ADR-004-pipeline-vs-realtime.md`
