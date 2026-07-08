# Use MarkItDown behind the knowledge ingestion boundary

KnowPulse will use `Microsoft.Extensions.DataIngestion.MarkItDown` to convert selected non-text attachments into Markdown during the knowledge ingestion reader stage. The supported default set is PDF, Word, PowerPoint, Excel, HTML, CSV, JSON, and XML.

The MarkItDown dependency must remain behind the project-owned `IKnowledgeDocumentMarkdownConverter` and `IKnowledgeIngestionPipeline` boundaries. Application contracts, domain models, UI DTOs, and vector-store records must not expose DataIngestion or MarkItDown types because those libraries remain preview/replaceable infrastructure.

The service requires a MarkItDown executable to be installed or configured through `RagAI:Ingestion:MarkItDownExecutablePath`. If conversion cannot run, the indexing job fails in the `reader` stage with a sanitized reason that does not include full attachment content.

Images, audio, video, and archives are not enabled by default. They require additional OCR, transcription, extraction, cost, and safety decisions, so the upload flow continues to label them as attachment-only until those capabilities are explicitly designed and verified.
