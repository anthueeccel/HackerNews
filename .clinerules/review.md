# Review workflow

Review the implementation of a feature against its spec, plan and the engineering rules.
Run this in a fresh task so the review does not inherit the implementation context.
Usage: `/review.md` and provide the feature folder, for example `specs/001-best-stories-api`.

## Ground rules

- You are a reviewer, not an implementer. Do NOT modify source code, tests or configuration.
- The only file you may create or overwrite is `<feature-folder>/review.md`.
- Base every finding on evidence: file path, line or symbol, and the requirement ID it relates to.
- Do not invent requirements. If something is not in the spec, report it as a suggestion, not a defect.

## Steps

1. Read `.clinerules/01-engineering-rules.md`, `<feature-folder>/spec.md` and `<feature-folder>/plan.md`.
2. Run `dotnet build` and `dotnet test`. Record the results (warnings, failures, test count).
3. Trace every requirement ID from `spec.md` (FR-*, NFR-*, EC-*) to the code and to a test. Mark each as Covered, Partial or Missing.
4. Check the engineering rules:
   - single production project plus one test project
   - nullable reference types, `record` types, `CancellationToken` passed through every layer
   - Options pattern, no magic numbers
   - no unnecessary NuGet packages
   - NUnit constraint model, NSubstitute, naming convention, no real network calls in tests
5. Check the non-functional requirement with extra care:
   - bounded parallelism (no unbounded `Task.WhenAll` over all IDs)
   - single-flight: concurrent requests for the same key cause one upstream call
   - failed or cancelled upstream calls are not left in the cache
   - cache TTLs come from configuration
6. Check error handling: invalid `n` returns 400 `ProblemDetails`, upstream failure or timeout returns 502/503 `ProblemDetails`, and no internal details leak.
7. Check simplicity: flag over-engineering (needless abstractions, extra layers, unused code) and under-engineering (missing validation, swallowed exceptions).
8. Check delivery artifacts: Dockerfile, `.dockerignore`, `.gitignore`, GitHub Actions workflow, README sections, and the link to `docs/ai-prompt.md`.
9. Check that `plan.md` tasks are ticked and match what was actually built.

## Output

Write `<feature-folder>/review.md` using this template, then summarize it in chat.

```
# Review: <feature name>

Date: <yyyy-mm-dd>
Verdict: Approved | Approved with comments | Changes requested

## Build and test
<results>

## Requirement traceability
| ID | Status | Evidence (code / test) |
|----|--------|------------------------|

## Findings
### Blocker
### Major
### Minor
### Suggestions

## What is done well
```

Severity guide:
- Blocker: a requirement is missing or broken, the build or tests fail, or the API can overload the upstream.
- Major: a likely bug, a missing test for important behavior, or a clear rule violation.
- Minor: style, naming, small readability issues.
- Suggestion: optional improvements, including README enhancement ideas.
