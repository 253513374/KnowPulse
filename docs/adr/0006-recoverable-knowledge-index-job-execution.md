# Execute knowledge indexing as recoverable background jobs

KnowPulse will use `KnowledgeIndexJob` as the durable execution fact between atomic publication and searchable RAG content. The HTTP host runs a configurable polling worker that consumes pending jobs and failed jobs whose retry time is due. Administrators and the worker share the same processor; UI actions must not simulate work by changing a status alone.

One execution follows `reader -> processor -> chunker -> persistence -> embedding -> vector-store`. The task persists its current stage, monotonic progress percentage, and last heartbeat. `Indexed` is written only after chunks, embedding audit records, Qdrant collection validation, and Qdrant upsert have succeeded.

Processing is idempotent at the task boundary. Re-delivering an `Indexed`, `Outdated`, or `Deleted` task is a no-op, and requesting another manual rebuild while the same version already has a pending or processing task returns that active task. Embedding records remain content-hash/model-version idempotent, while Qdrant uses chunk IDs as deterministic upsert IDs.

Failed attempts stay on the same task for automatic retries and use exponential backoff capped at 30 minutes. Once the configured retry budget is exhausted, automatic execution stops. Manual retry or recovery creates a new pending task with `RetryOfJobId` pointing to the failed or stuck task, preserving both the original failure and the recovery lineage.

The worker is enabled by `KnowledgeIndexing:Worker:IsEnabled` and processes small sequential batches. This P3 decision requires an EF Core migration for progress, heartbeat, stage, and retry-lineage fields. It extends the asynchronous boundary established in ADR 0005; publication remains successful when a pending task is durable, while the management state remains `Publishing` until this worker completes the task.
