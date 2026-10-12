# AI kickoff prompt

This repository was built with AI assistance (Cline in VS Code) using a spec-driven workflow.
The standing rules are in `.clinerules/`, the requirements are in `specs/001-best-stories-api/spec.md`,
and the plan is produced by the AI and approved by a human before any code is written.

## Kickoff prompt (paste into Cline in PLAN mode)

```
Read `.clinerules/01-engineering-rules.md` and `specs/001-best-stories-api/spec.md`.

You are in PLAN mode. Do not write production code yet.

1. Inspect the workspace and the installed .NET SDK.
2. Ask me about anything in the spec that is ambiguous.
3. Write `specs/001-best-stories-api/plan.md` with:
   - Design decisions (the spec already fixes Minimal API; list any other decision with a short justification)
   - Final folder structure and the list of files to create
   - Key types and their responsibilities, with signatures
   - Caching and single-flight approach
   - Configuration keys with defaults
   - Test list, mapped to requirement IDs from the spec
   - Ordered task checklist (small tasks, each ending with a passing build)
4. Stop and wait for my approval before switching to ACT mode.
```

## Implementation prompt (after the plan is approved)

```
The plan in `specs/001-best-stories-api/plan.md` is approved. Switch to ACT mode.
Implement the tasks in order, tick each one in plan.md when done, and commit after each logical step.
Finish only when `dotnet build` and `dotnet test` pass.
```

## Review prompt (in a fresh task, after implementation)

```
/review.md specs/001-best-stories-api
```

## Starting a new feature

Create `specs/NNN-feature-name/spec.md` (copy the structure of an existing spec), then reuse the kickoff prompt
with the new folder name.
