# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Layout

This is a single-context repo.

Read these when present:

- `CONTEXT.md` at the repo root
- `docs/adr/` at the repo root

The repo currently also has project-level architecture and planning docs under `docs/`, including `docs/technical-architecture.md`, `docs/system-architecture.md`, and MVP acceptance/task-breakdown documents. Use them as supporting context when they touch the area being changed.

If `CONTEXT.md` or `docs/adr/` do not exist, proceed silently. Do not flag their absence or suggest creating them upfront. The `domain-modeling` skill creates them lazily when terms or decisions actually get resolved.

## File structure

Single-context repo:

```text
/
|-- CONTEXT.md
|-- docs/
|   `-- adr/
|       |-- 0001-example-decision.md
|       `-- 0002-example-decision.md
`-- src/
```

## Use the glossary's vocabulary

When your output names a domain concept in an issue title, refactor proposal, hypothesis, or test name, use the term as defined in `CONTEXT.md`. Do not drift to synonyms the glossary explicitly avoids.

If the concept you need is not in the glossary yet, that is a signal: either you are inventing language the project does not use, or there is a real gap to note for `domain-modeling`.

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> Contradicts ADR-0007 because this change would alter the recorded decision; reopen the decision before implementing.
