# Engineering rules

These rules apply to every task in this repository. They are stable: feature-specific requirements live in `specs/`.

## Role

Act as a senior .NET engineer. Prefer the simplest solution that fully solves the problem and is easy to read. Do not over-engineer.

## Language

All code, comments, commit messages, documentation and chat answers are in English.

## Spec-driven workflow

1. Every feature has its own folder: `specs/NNN-feature-name/` (zero-padded, sequential, kebab-case).
2. The folder contains:
   - `spec.md`: what and why (source of truth)
   - `plan.md`: how, plus an ordered task checklist (written by you in PLAN mode, approved by the human)
   - `review.md`: output of the review workflow (optional)
3. Always start in PLAN mode. Read `spec.md`, then write `plan.md`. Do not write production code until the human approves the plan.
4. If the spec is ambiguous, ask before planning. Never silently invent requirements.
5. If implementation requires a different design than `plan.md`, update `plan.md` first and tell the human.
6. Tick tasks in `plan.md` as they are completed.

## Technology

- Target the latest installed LTS .NET SDK (check with `dotnet --version`; .NET 10 if available, otherwise .NET 8).
- Single production project plus one test project. Do not split into Domain/Application/Infrastructure projects. Show separation of concerns with folders and interfaces.
- Enable nullable reference types and implicit usings.
- Use `record` types for DTOs and models.
- Use `async`/`await` end to end and pass `CancellationToken` through every layer.
- Use the Options pattern for configuration. No magic numbers.
- Avoid extra NuGet packages unless clearly justified. Prefer built-in framework features.

## Code style

- Short methods, clear names, one responsibility per class.
- Comments only where they explain why, never what.
- No dead code, no commented-out code, no TODOs left behind.

## Testing

- NUnit with the constraint model: `Assert.That(actual, Is...)`.
- NSubstitute for mocking (a small hand-written fake is fine where simpler).
- Arrange / Act / Assert, test names as `Method_Scenario_ExpectedResult`.
- Use `[TestCase]` for parameterized tests.
- Tests must never call external services. Replace HTTP calls with a fake `HttpMessageHandler`.

## Definition of done

- `dotnet build` has no warnings and `dotnet test` passes.
- Every requirement ID in `spec.md` is covered by code and, where testable, by a test.
- The README is updated if behavior, configuration or run instructions changed.

## Git

- Small, logical commits using Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`).
- Reference the feature folder in the commit body when useful (for example `Spec: 001-best-stories-api`).
