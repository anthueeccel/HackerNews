# Specs

One folder per feature, numbered in the order the work was done, so the history of the project is easy to follow.

```
specs/
  001-best-stories-api/
    spec.md     what and why (source of truth)
    plan.md     how, plus the task checklist (AI-written, human-approved)
    review.md   result of the review workflow
```

Conventions:

- Folder name: `NNN-kebab-case-feature-name`, with NNN zero-padded and sequential.
- Never renumber old folders. A change to a shipped feature is a new folder that references the old one.
- `spec.md` changes first, then `plan.md` follows.
