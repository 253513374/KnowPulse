# Publish knowledge atomically with an explicit access policy

KnowPulse will treat knowledge publication as one database transaction that replaces the complete access-policy snapshot, switches the current published version, marks previous index facts and active chunks as outdated, and creates the new publish index job.

ADR 0009 adds an exception for a valid, explicitly shared personal version: its independent school use can retain old index facts when the owner prepares a private revision. Unshared versions still become outdated, the transaction boundary remains unchanged, and retained indexes never substitute for current reader and use authorization.

Every new publish command must contain at least one access rule. A policy may contain one `All` rule or one or more targeted department, position, role, or user rules, but it cannot combine `All` with targeted rules or repeat the same target. Publishing requires both version-management and permission-scope permissions.

Index execution remains asynchronous. A successful publish means the version, policy, and pending index task are durable together; it does not mean embedding has finished. The management read model continues to expose this intermediate state as `Publishing` until the task becomes `Indexed`.

Attachments are uploaded before the final publish command and are not part of the relational transaction. If attachment upload or publication fails, the knowledge item, version, and successfully uploaded attachments remain as a recoverable draft. The system must not expose the version as published unless the access policy and index job were committed with it.

Historical knowledge with no permission-scope rows remains readable through the existing empty-scope compatibility rule. The management UI labels this state explicitly. Republishing normalizes it to a persisted policy, and administrators can persist an explicit `All` rule without changing effective access.

This decision reuses the existing entities and does not require a database migration. It supersedes the P1 write-side deferral described in `docs/adr/0004-knowledge-management-read-model.md`.
