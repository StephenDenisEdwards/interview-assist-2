# Documentation Instructions

This file provides detailed guidelines for creating and maintaining documentation in the Interview Assist repository.

## Documentation Structure

```
documentation/docs/
├── index.md              # Navigation index - update when adding new docs
├── architecture/         # Formal architecture documentation
│   ├── SAD.md           # Software Architecture Document
│   └── decisions/       # Architecture Decision Records
│       └── README.md    # ADR template and index
├── design/              # Design documents for features/subsystems
├── domain/              # Domain knowledge and external integrations
├── ideas/               # Exploratory concepts and proposals
├── operations/          # Operational and configuration guides
└── plans/
    ├── in-progress/     # Active improvement plans
    ├── completed/       # Finished improvement plans
    └── dropped/         # Abandoned plans (with rationale)
```

## Document Types

### Architecture Decision Records (ADRs)

**Location:** `documentation/docs/architecture/decisions/ADR-XXX-short-title.md`

**When to create:**
- Adding a new external dependency or library
- Changing communication protocols or API integrations
- Modifying the audio processing pipeline
- Adding new platform-specific implementations
- Making significant changes to the concurrency model
- Choosing between multiple viable technical approaches

**Template (Nygard format):**
```markdown
# ADR-XXX: Title

## Status
Proposed | Accepted | Deprecated | Superseded by ADR-YYY

## Context
What is the issue that we're seeing that is motivating this decision or change?

## Decision
What is the change that we're proposing and/or doing?

## Consequences
What becomes easier or more difficult to do because of this change?
```

**Naming:** Use sequential numbers (001, 002, etc.) and kebab-case titles.

### Software Architecture Document (SAD)

**Location:** `documentation/docs/architecture/SAD.md`

**When to update:**
- Adding new projects to the solution
- Creating new key interfaces or abstractions
- Changing the building block structure
- Modifying runtime behavior (update sequence diagrams)
- Adding new crosscutting concerns

### Design Documents

**Location:** `documentation/docs/design/DESIGN-*.md`

**When to create:**
- Designing a complex feature before implementation
- Documenting a subsystem or pipeline
- Capturing design rationale for non-trivial components

**Suggested sections:**
- Overview/Goal
- Requirements/Constraints
- Design approach
- Component interactions (diagrams if helpful)
- Alternatives considered

### Domain Knowledge

**Location:** `documentation/docs/domain/`

**When to create:**
- Integrating with external services (APIs, SDKs)
- Implementing algorithms or detection logic
- Capturing domain-specific concepts

**Examples:**
- `deepgram-overview.md` - Deepgram API integration details
- `heuristic-question-detector.md` - Question detection algorithm

### Operations Guides

**Location:** `documentation/docs/operations/`

**When to create:**
- Documenting configuration options
- Rate limiting or quota management
- Deployment procedures
- Troubleshooting guides

**Examples:**
- `appsettings.md` - Configuration reference
- `rate-limiting.md` - API rate limit handling

### Improvement Plans

**Location:** `documentation/docs/plans/in-progress/IMPROVEMENT-PLAN-XXXX.md`

**When to create:**
- Multi-task refactoring work
- Feature implementation spanning multiple files
- Technical debt paydown

**Template:**
```markdown
# IMPROVEMENT-PLAN-XXXX: Title

**Created:** YYYY-MM-DD
**Status:** In Progress | Completed

## Goal
Brief description of what this plan accomplishes.

## Progress Tracker

| Step | Description | Status | Notes |
| ---- | ----------- | ------ | ----- |
| 1 | Task description | Not Started / In Progress / Done | Implementation details or blockers |
| 2 | Task description | Not Started | |

**Last updated:** YYYY-MM-DD

## Tasks

### Phase 1: Description
- [ ] Task 1
- [ ] Task 2

### Phase 2: Description
- [ ] Task 3

## Files Affected
- `path/to/file1.cs`
- `path/to/file2.cs`

## Implementation Summary
(Add after completion)
```

**Progress Tracker guidelines:**
- Every improvement plan **must** include a Progress Tracker table
- Update the table and "Last updated" date as work progresses
- Use statuses: `Not Started`, `In Progress`, `Done`
- Add brief notes for completed or blocked steps (what was done, what file, or what's blocking)

**Lifecycle:**
1. Create in `plans/in-progress/` with next available number
2. Update tasks as work progresses
3. When complete:
   - Add Implementation Summary section
   - Update status to "Completed"
   - Move to `plans/completed/`
   - Update solution file references
4. If dropped:
   - Add a "Reason Dropped" section explaining why (even one sentence is sufficient)
   - Update status to "Dropped"
   - Move to `plans/dropped/`
   - Update solution file references

### Ideas

**Location:** `documentation/docs/ideas/YYYY-MM-DD-short-title.md`

**When to create:**
- Exploratory design concepts not yet ready for implementation
- Feature proposals that need further research or discussion
- Spike results or proof-of-concept findings

**Naming:** Use date prefix (`YYYY-MM-DD`) followed by kebab-case title.

**Lifecycle:**
1. Create in `ideas/` with the current date prefix
2. If the idea is approved for implementation, create an improvement plan in `plans/in-progress/` and reference the original idea document
3. If the idea is no longer relevant after 90 days of inactivity, add a note at the top: `**Status:** Archived — [reason]`

## Solution File Integration

All documentation must be added to `interview-assist-2.sln` to appear in Visual Studio.

**Solution folder GUIDs:**
| Folder | GUID |
|--------|------|
| docs | `{78751797-6A9E-46A0-95BA-6D7B066ADF83}` |
| architecture | `{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}` |
| decisions | `{B2C3D4E5-F6A7-8901-BCDE-F12345678901}` |
| design | `{2A0CA0AD-8899-41B6-A42E-9861D2EB1C36}` |
| operations | `{62F3D4C7-6860-46B6-B20C-CFEB323E97DE}` |
| domain | `{DAE56BC8-6701-48C6-BB0E-11AEA66F8D9A}` |
| plans | `{3B4C5D6E-F7A8-9012-CDEF-234567890ABC}` |
| in-progress | `{4C5D6E7F-A8B9-0123-DEF0-345678901BCD}` |
| completed | `{5D6E7F8A-B9C0-1234-EF01-456789012CDE}` |
| dropped | `{75C3B321-7AA5-4F14-8F3E-D41B866363C7}` |
| ideas | `{7F3A1B2C-D4E5-6789-ABCD-0123456789EF}` |
| instructions | `{1A6FC9A8-C914-449B-BE9A-B3DA4BB7D8CC}` |

**Adding a file to the solution:**

1. Find the appropriate Project section in `interview-assist-2.sln`
2. Add the file path in the `ProjectSection(SolutionItems)` block:
```
documentation\docs\{folder}\{filename}.md = documentation\docs\{folder}\{filename}.md
```

## Navigation Index

After adding new documentation, update `documentation/docs/index.md` to include a link in the appropriate section.

## File Naming Convention

All documentation files use **lowercase kebab-case**:
- Domain docs: `deepgram-overview.md`, `heuristic-question-detector.md`
- Operations docs: `getting-started.md`, `rate-limiting.md`, `appsettings.md`
- Design docs: `DESIGN-` prefix with kebab-case title (e.g., `DESIGN-utterance-intent-pipeline.md`)
- ADRs: `ADR-XXX-` prefix with kebab-case title (e.g., `ADR-007-multi-strategy-intent-detection.md`)
- Improvement plans: `IMPROVEMENT-PLAN-XXXX-` prefix with kebab-case title
- Ideas: `YYYY-MM-DD-` prefix with kebab-case title

## Writing Guidelines

- Use clear, concise language
- Include code examples where helpful
- Keep documents focused on a single topic
- Update related documents when making changes
- Prefer diagrams for complex flows (Mermaid supported)
