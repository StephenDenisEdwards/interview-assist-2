#prompt:review-code.prompt.md

Use @workspace as the source of truth for the solution contents. Do not ask me for a repository link.
If you can’t access some files/projects through @workspace, continue anyway and list exactly what you could and couldn’t inspect.

Start now by enumerating the projects you can see in @workspace, then produce the report.


Review the entire solution as a read-only exercise and produce a written report.

Scope:
- Consider all projects in the solution
- Analyze architecture, structure, and cross-project interactions
- Do not limit the review to currently open files

Rules:
- DO NOT generate, modify, or suggest code changes
- DO NOT produce code snippets
- DO NOT propose concrete refactors or implementations
- This is an analytical review only

Focus areas:
- Architectural consistency and layering
- Separation of concerns
- Dependency direction and coupling
- Error handling strategy
- Async / concurrency usage (where applicable)
- Test coverage and test intent
- Configuration and environment handling
- Maintainability and long-term risks
- Signs of AI-generated or copy-pasted code (if detectable)

Identify:
- Strengths of the current solution
- Potential risks or design smells
- Inconsistencies across projects or layers
- Areas where intent is unclear or undocumented
- Scaling, reliability, or operability concerns

Output format:
- Title: "Solution Code Review Report"
- Sections:
  1. Executive Summary (high-level assessment)
  2. Architectural Observations
  3. Maintainability & Code Health
  4. Testing & Quality Signals
  5. Risk Areas & Technical Debt Indicators
  6. Questions or Clarifications Needed

Tone:
- Professional, neutral, and precise
- Assume the audience is senior engineers or architects
- Prefer evidence-based observations over speculation

If full-solution analysis is not possible due to context limits:
- State explicitly what was and was not reviewed
- Do not guess or hallucinate missing information
