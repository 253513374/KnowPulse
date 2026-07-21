# Use an aggregated read model for knowledge management

KnowPulse will use `/knowledge` as the primary knowledge-management list and `/knowledge/{id}` as the knowledge-centered detail workspace. The existing `/knowledge/items` route remains available for advanced write operations during P1.

The management list and detail read from a dedicated application service that aggregates the existing knowledge item, category, version, permission scope, index job, attachment, and feedback facts. It must continue to apply the same knowledge-access evaluation as the existing item service.

The UI-facing status is computed at query time and is not persisted:

- `Draft`: no current published version.
- `Publishing`: the current published version has a pending or processing index job.
- `Ready`: the current published version has an indexed latest job.
- `NeedsAttention`: the current published version has no valid index result, or the latest job failed, became outdated, or was deleted.
- `Withdrawn`, `Archived`, and `Disabled`: derived from the item or latest version lifecycle.

P1 does not change the MarkItDown ingestion boundary, index pipeline, version lifecycle, permission persistence, or upload transaction. P2 adds the write-side publication boundary in `docs/adr/0005-atomic-knowledge-publish-with-explicit-access-policy.md` without changing the ingestion implementation.
