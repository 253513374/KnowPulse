<#
.SYNOPSIS
查询 KnowPulse 审计日志中的最近请求、操作汇总和敏感参数命中情况。

.DESCRIPTION
默认通过 docker compose 访问本地 postgres 服务；传入 -LocalPsql 时改用本机 psql。
该脚本只执行只读 SQL，用于 MVP-1 RAG/知识库和 MVP-2 题库、AI 出题、考试闭环后的审计链路验证。
MVP-3 增加学习路径和知识反馈闭环检查，验证反馈状态、处理人、关联版本和敏感字段边界。
#>
[CmdletBinding()]
param(
    [int]$Limit = 20,
    [string]$Database = "KnowPulse",
    [string]$PostgresUser = "root",
    [string]$PostgresPassword = "myPassword",
    [string]$PostgresHost = "localhost",
    [int]$PostgresPort = 5432,
    [string]$ServiceNameLike = "%KnowPulse%",
    [string]$MethodNameLike = "%",
    [switch]$LocalPsql,
    [switch]$FailOnSensitiveHits
)

$ErrorActionPreference = "Stop"

if ($Limit -lt 1 -or $Limit -gt 500) {
    throw "Limit must be between 1 and 500."
}

# 将用户输入的 LIKE 条件转义为 SQL 字符串字面量，避免单引号破坏查询语句。
function ConvertTo-SqlLiteral {
    param([string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

# 执行审计 SQL；默认进入 docker compose 中的 postgres，-LocalPsql 时使用本机 psql。
function Invoke-AuditSql {
    param([string]$Sql)

    if ($LocalPsql) {
        $oldPassword = $env:PGPASSWORD
        $env:PGPASSWORD = $PostgresPassword
        try {
            & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $Database -v ON_ERROR_STOP=1 -c $Sql
        }
        finally {
            $env:PGPASSWORD = $oldPassword
        }
    }
    else {
        & docker compose exec -T postgres psql -U $PostgresUser -d $Database -v ON_ERROR_STOP=1 -c $Sql
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Audit SQL query failed with exit code $LASTEXITCODE."
    }
}

function Invoke-AuditScalar {
    param([string]$Sql)

    if ($LocalPsql) {
        $oldPassword = $env:PGPASSWORD
        $env:PGPASSWORD = $PostgresPassword
        try {
            $output = & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $Database -v ON_ERROR_STOP=1 -t -A -c $Sql
        }
        finally {
            $env:PGPASSWORD = $oldPassword
        }
    }
    else {
        $output = & docker compose exec -T postgres psql -U $PostgresUser -d $Database -v ON_ERROR_STOP=1 -t -A -c $Sql
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Audit SQL scalar query failed with exit code $LASTEXITCODE."
    }

    $lines = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -eq 0) {
        return ""
    }

    return $lines[-1].Trim()
}

$serviceNameLikeSql = ConvertTo-SqlLiteral $ServiceNameLike
$methodNameLikeSql = ConvertTo-SqlLiteral $MethodNameLike

$recentActionsSql = @"
SELECT
    l."ExecutionTime",
    l."TenantId",
    l."UserId",
    l."HttpMethod",
    l."Url",
    l."HttpStatusCode",
    a."ServiceName",
    a."MethodName",
    a."ExecutionDuration"
FROM "AbpAuditLogs" l
LEFT JOIN "AbpAuditLogActions" a ON a."AuditLogId" = l."Id"
WHERE (a."ServiceName" ILIKE $serviceNameLikeSql OR a."ServiceName" IS NULL)
  AND (a."MethodName" ILIKE $methodNameLikeSql OR a."MethodName" IS NULL)
ORDER BY l."ExecutionTime" DESC
LIMIT $Limit;
"@

$operationSummarySql = @"
SELECT
    a."ServiceName",
    a."MethodName",
    COUNT(*) AS "Count",
    MAX(l."ExecutionTime") AS "LastExecutionTime"
FROM "AbpAuditLogs" l
JOIN "AbpAuditLogActions" a ON a."AuditLogId" = l."Id"
WHERE a."ServiceName" ILIKE $serviceNameLikeSql
GROUP BY a."ServiceName", a."MethodName"
ORDER BY "LastExecutionTime" DESC, "Count" DESC
LIMIT $Limit;
"@

$sensitiveScanSql = @"
SELECT 'ActionParameters' AS "Location", COUNT(*) AS "Hits"
FROM "AbpAuditLogActions"
WHERE COALESCE("Parameters", '') ILIKE '%ContentText%'
   OR COALESCE("Parameters", '') ILIKE '%IRemoteStreamContent%'
   OR COALESCE("Parameters", '') ILIKE '%RemoteStreamContent%'
UNION ALL
SELECT 'EntityPropertyChanges' AS "Location", COUNT(*) AS "Hits"
FROM "AbpEntityPropertyChanges"
WHERE "PropertyName" ILIKE '%ContentText%'
   OR COALESCE("OriginalValue", '') ILIKE '%ContentText%'
   OR COALESCE("NewValue", '') ILIKE '%ContentText%';
"@

$ragRecentCallsSql = @"
SELECT
    "CreationTime",
    "TenantId",
    "UserId",
    "CallType",
    "Status",
    "ModelProvider",
    "ModelName",
    "ModelVersion",
    "TopK",
    "RetrievedChunkCount",
    "CitationCount",
    "PromptTokenCount",
    "CompletionTokenCount",
    "TotalTokenCount",
    "EstimatedCostUsd",
    "FailureStage",
    "FailureReason"
FROM "AppRagCallLogs"
ORDER BY "CreationTime" DESC
LIMIT $Limit;
"@

$ragCitationChainSql = @"
SELECT
    l."CreationTime",
    l."UserId",
    l."CallType",
    l."Status",
    l."ModelVersion",
    c."KnowledgeItemId",
    c."KnowledgeVersionId",
    c."ChunkId",
    c."Rank",
    c."Score",
    c."SourceTitle",
    c."SourcePage"
FROM "AppRagCitationRecords" c
JOIN "AppRagCallLogs" l ON l."Id" = c."RagCallLogId"
ORDER BY l."CreationTime" DESC, c."Rank" ASC
LIMIT $Limit;
"@

$ragSensitiveScanSql = @"
SELECT 'RagCallLogs.QuerySummary' AS "Location", COUNT(*) AS "Hits"
FROM "AppRagCallLogs"
WHERE LENGTH(COALESCE("QuerySummary", '')) > 256
   OR COALESCE("QuerySummary", '') ILIKE '%ContentText%'
   OR COALESCE("QuerySummary", '') ILIKE '%IRemoteStreamContent%'
   OR COALESCE("QuerySummary", '') ILIKE '%RemoteStreamContent%'
   OR COALESCE("QuerySummary", '') ILIKE '%FullPrompt%'
   OR COALESCE("QuerySummary", '') ILIKE '%SystemPrompt%'
   OR COALESCE("QuerySummary", '') ILIKE '%AttachmentContent%'
UNION ALL
SELECT 'RagCallLogs.FailureReason' AS "Location", COUNT(*) AS "Hits"
FROM "AppRagCallLogs"
WHERE COALESCE("FailureReason", '') ILIKE '%ContentText%'
   OR COALESCE("FailureReason", '') ILIKE '%IRemoteStreamContent%'
   OR COALESCE("FailureReason", '') ILIKE '%RemoteStreamContent%'
   OR COALESCE("FailureReason", '') ILIKE '%FullPrompt%'
   OR COALESCE("FailureReason", '') ILIKE '%SystemPrompt%'
   OR COALESCE("FailureReason", '') ILIKE '%AttachmentContent%'
UNION ALL
SELECT 'RagCitationRecords.QuotePreview' AS "Location", COUNT(*) AS "Hits"
FROM "AppRagCitationRecords"
WHERE LENGTH(COALESCE("QuotePreview", '')) > 180
   OR COALESCE("QuotePreview", '') ILIKE '%ContentText%'
   OR COALESCE("QuotePreview", '') ILIKE '%IRemoteStreamContent%'
   OR COALESCE("QuotePreview", '') ILIKE '%RemoteStreamContent%'
   OR COALESCE("QuotePreview", '') ILIKE '%FullPrompt%'
   OR COALESCE("QuotePreview", '') ILIKE '%SystemPrompt%'
   OR COALESCE("QuotePreview", '') ILIKE '%AttachmentContent%';
"@

$aiQuestionRecentCallsSql = @"
SELECT
    "CreationTime",
    "TenantId",
    "TaskId",
    "PromptTemplateId",
    "PromptVersion",
    "Status",
    "ModelProvider",
    "ModelName",
    "ModelVersion",
    "TopK",
    "RetrievedChunkCount",
    "CandidateCount",
    "PromptTokenCount",
    "CompletionTokenCount",
    "TotalTokenCount",
    "ExecutionDurationMs",
    "FailureStage",
    "FailureReason"
FROM "AppAiQuestionModelCallLogs"
ORDER BY "CreationTime" DESC
LIMIT $Limit;
"@

$questionReviewTrailSql = @"
SELECT
    "CreationTime",
    "TenantId",
    "QuestionId",
    "ReviewerUserId",
    "Action",
    "FromStatus",
    "ToStatus",
    "ReviewTime"
FROM "AppQuestionReviews"
ORDER BY "CreationTime" DESC
LIMIT $Limit;
"@

$knowledgeFeedbackTrailSql = @"
SELECT
    "CreationTime",
    "TenantId",
    "KnowledgeItemId",
    "KnowledgeVersionId",
    "SubmitterUserId",
    "Type",
    "Status",
    "HandlerUserId",
    "HandledTime",
    "LinkedKnowledgeVersionId",
    "ClosedTime"
FROM "AppKnowledgeFeedbacks"
ORDER BY "CreationTime" DESC
LIMIT $Limit;
"@

$knowledgeFeedbackAuditActionsSql = @"
SELECT
    l."ExecutionTime",
    l."TenantId",
    l."UserId",
    l."HttpMethod",
    l."Url",
    l."HttpStatusCode",
    a."ServiceName",
    a."MethodName",
    a."ExecutionDuration"
FROM "AbpAuditLogs" l
JOIN "AbpAuditLogActions" a ON a."AuditLogId" = l."Id"
WHERE a."ServiceName" ILIKE '%KnowledgeFeedback%'
   OR a."MethodName" ILIKE '%Feedback%'
ORDER BY l."ExecutionTime" DESC
LIMIT $Limit;
"@

$knowledgeFeedbackSensitiveScanSql = @"
SELECT 'KnowledgeFeedbackAuditActions.ContentText' AS "Location", COUNT(*) AS "Hits"
FROM "AbpAuditLogActions"
WHERE (COALESCE("ServiceName", '') ILIKE '%KnowledgeFeedback%' OR COALESCE("MethodName", '') ILIKE '%Feedback%')
  AND (
      COALESCE("Parameters", '') ILIKE '%ContentText%'
      OR COALESCE("Parameters", '') ILIKE '%IRemoteStreamContent%'
      OR COALESCE("Parameters", '') ILIKE '%RemoteStreamContent%'
      OR COALESCE("Parameters", '') ILIKE '%AttachmentContent%'
  )
UNION ALL
SELECT 'KnowledgeFeedbacks.DescriptionLength' AS "Location", COUNT(*) AS "Hits"
FROM "AppKnowledgeFeedbacks"
WHERE LENGTH(COALESCE("Description", '')) > 2048
UNION ALL
SELECT 'KnowledgeFeedbacks.ResolutionLength' AS "Location", COUNT(*) AS "Hits"
FROM "AppKnowledgeFeedbacks"
WHERE LENGTH(COALESCE("Resolution", '')) > 2048;
"@

$examScoringClosureSql = @"
SELECT
    a."CreationTime",
    a."TenantId",
    a."UserId",
    a."PlanId",
    a."PaperId",
    a."Status",
    a."StartedTime",
    a."SubmittedTime",
    a."ScoredTime",
    s."ObtainedScore",
    s."TotalScore",
    s."PassingScore",
    s."ScoringVersion",
    COUNT(w."Id") AS "WrongQuestionCount"
FROM "AppExamAttempts" a
LEFT JOIN "AppExamScores" s ON s."AttemptId" = a."Id"
LEFT JOIN "AppWrongQuestionRecords" w ON w."ScoreId" = s."Id"
GROUP BY
    a."Id",
    a."CreationTime",
    a."TenantId",
    a."UserId",
    a."PlanId",
    a."PaperId",
    a."Status",
    a."StartedTime",
    a."SubmittedTime",
    a."ScoredTime",
    s."ObtainedScore",
    s."TotalScore",
    s."PassingScore",
    s."ScoringVersion"
ORDER BY a."CreationTime" DESC
LIMIT $Limit;
"@

$mvp2SensitiveScanSql = @"
SELECT 'AiQuestionModelCallLogs.QuerySummary' AS "Location", COUNT(*) AS "Hits"
FROM "AppAiQuestionModelCallLogs"
WHERE LENGTH(COALESCE("QuerySummary", '')) > 256
   OR COALESCE("QuerySummary", '') ILIKE '%ContentText%'
   OR COALESCE("QuerySummary", '') ILIKE '%IRemoteStreamContent%'
   OR COALESCE("QuerySummary", '') ILIKE '%RemoteStreamContent%'
   OR COALESCE("QuerySummary", '') ILIKE '%FullPrompt%'
   OR COALESCE("QuerySummary", '') ILIKE '%SystemPrompt%'
   OR COALESCE("QuerySummary", '') ILIKE '%AttachmentContent%'
UNION ALL
SELECT 'AiQuestionModelCallLogs.FailureReason' AS "Location", COUNT(*) AS "Hits"
FROM "AppAiQuestionModelCallLogs"
WHERE COALESCE("FailureReason", '') ILIKE '%ContentText%'
   OR COALESCE("FailureReason", '') ILIKE '%IRemoteStreamContent%'
   OR COALESCE("FailureReason", '') ILIKE '%RemoteStreamContent%'
   OR COALESCE("FailureReason", '') ILIKE '%FullPrompt%'
   OR COALESCE("FailureReason", '') ILIKE '%SystemPrompt%'
   OR COALESCE("FailureReason", '') ILIKE '%AttachmentContent%'
UNION ALL
SELECT 'AiQuestionCandidateCitations.SourceExcerpt' AS "Location", COUNT(*) AS "Hits"
FROM "AppAiQuestionCandidateCitations"
WHERE LENGTH(COALESCE("SourceExcerpt", '')) > 512
   OR COALESCE("SourceExcerpt", '') ILIKE '%ContentText%'
   OR COALESCE("SourceExcerpt", '') ILIKE '%IRemoteStreamContent%'
   OR COALESCE("SourceExcerpt", '') ILIKE '%RemoteStreamContent%'
   OR COALESCE("SourceExcerpt", '') ILIKE '%FullPrompt%'
   OR COALESCE("SourceExcerpt", '') ILIKE '%SystemPrompt%'
   OR COALESCE("SourceExcerpt", '') ILIKE '%AttachmentContent%'
UNION ALL
SELECT 'QuestionSources.SourceExcerpt' AS "Location", COUNT(*) AS "Hits"
FROM "AppQuestionSources"
WHERE LENGTH(COALESCE("SourceExcerpt", '')) > 512
   OR COALESCE("SourceExcerpt", '') ILIKE '%ContentText%'
   OR COALESCE("SourceExcerpt", '') ILIKE '%IRemoteStreamContent%'
   OR COALESCE("SourceExcerpt", '') ILIKE '%RemoteStreamContent%'
   OR COALESCE("SourceExcerpt", '') ILIKE '%FullPrompt%'
   OR COALESCE("SourceExcerpt", '') ILIKE '%SystemPrompt%'
   OR COALESCE("SourceExcerpt", '') ILIKE '%AttachmentContent%';
"@

$sensitiveTotalSql = @"
WITH hits AS (
    SELECT COUNT(*)::bigint AS hits
    FROM "AbpAuditLogActions"
    WHERE COALESCE("Parameters", '') ILIKE '%ContentText%'
       OR COALESCE("Parameters", '') ILIKE '%IRemoteStreamContent%'
       OR COALESCE("Parameters", '') ILIKE '%RemoteStreamContent%'
    UNION ALL
    SELECT COUNT(*)::bigint AS hits
    FROM "AbpEntityPropertyChanges"
    WHERE "PropertyName" ILIKE '%ContentText%'
       OR COALESCE("OriginalValue", '') ILIKE '%ContentText%'
       OR COALESCE("NewValue", '') ILIKE '%ContentText%'
    UNION ALL
    SELECT COUNT(*)::bigint AS hits
    FROM "AppRagCallLogs"
    WHERE LENGTH(COALESCE("QuerySummary", '')) > 256
       OR COALESCE("QuerySummary", '') ILIKE '%ContentText%'
       OR COALESCE("QuerySummary", '') ILIKE '%IRemoteStreamContent%'
       OR COALESCE("QuerySummary", '') ILIKE '%RemoteStreamContent%'
       OR COALESCE("QuerySummary", '') ILIKE '%FullPrompt%'
       OR COALESCE("QuerySummary", '') ILIKE '%SystemPrompt%'
       OR COALESCE("QuerySummary", '') ILIKE '%AttachmentContent%'
    UNION ALL
    SELECT COUNT(*)::bigint AS hits
    FROM "AppRagCallLogs"
    WHERE COALESCE("FailureReason", '') ILIKE '%ContentText%'
       OR COALESCE("FailureReason", '') ILIKE '%IRemoteStreamContent%'
       OR COALESCE("FailureReason", '') ILIKE '%RemoteStreamContent%'
       OR COALESCE("FailureReason", '') ILIKE '%FullPrompt%'
       OR COALESCE("FailureReason", '') ILIKE '%SystemPrompt%'
       OR COALESCE("FailureReason", '') ILIKE '%AttachmentContent%'
    UNION ALL
    SELECT COUNT(*)::bigint AS hits
    FROM "AppRagCitationRecords"
    WHERE LENGTH(COALESCE("QuotePreview", '')) > 180
       OR COALESCE("QuotePreview", '') ILIKE '%ContentText%'
       OR COALESCE("QuotePreview", '') ILIKE '%IRemoteStreamContent%'
       OR COALESCE("QuotePreview", '') ILIKE '%RemoteStreamContent%'
       OR COALESCE("QuotePreview", '') ILIKE '%FullPrompt%'
       OR COALESCE("QuotePreview", '') ILIKE '%SystemPrompt%'
       OR COALESCE("QuotePreview", '') ILIKE '%AttachmentContent%'
    UNION ALL
    SELECT COUNT(*)::bigint AS hits
    FROM "AppAiQuestionModelCallLogs"
    WHERE LENGTH(COALESCE("QuerySummary", '')) > 256
       OR COALESCE("QuerySummary", '') ILIKE '%ContentText%'
       OR COALESCE("QuerySummary", '') ILIKE '%IRemoteStreamContent%'
       OR COALESCE("QuerySummary", '') ILIKE '%RemoteStreamContent%'
       OR COALESCE("QuerySummary", '') ILIKE '%FullPrompt%'
       OR COALESCE("QuerySummary", '') ILIKE '%SystemPrompt%'
       OR COALESCE("QuerySummary", '') ILIKE '%AttachmentContent%'
    UNION ALL
    SELECT COUNT(*)::bigint AS hits
    FROM "AppAiQuestionModelCallLogs"
    WHERE COALESCE("FailureReason", '') ILIKE '%ContentText%'
       OR COALESCE("FailureReason", '') ILIKE '%IRemoteStreamContent%'
       OR COALESCE("FailureReason", '') ILIKE '%RemoteStreamContent%'
       OR COALESCE("FailureReason", '') ILIKE '%FullPrompt%'
       OR COALESCE("FailureReason", '') ILIKE '%SystemPrompt%'
       OR COALESCE("FailureReason", '') ILIKE '%AttachmentContent%'
    UNION ALL
    SELECT COUNT(*)::bigint AS hits
    FROM "AppAiQuestionCandidateCitations"
    WHERE LENGTH(COALESCE("SourceExcerpt", '')) > 512
       OR COALESCE("SourceExcerpt", '') ILIKE '%ContentText%'
       OR COALESCE("SourceExcerpt", '') ILIKE '%IRemoteStreamContent%'
       OR COALESCE("SourceExcerpt", '') ILIKE '%RemoteStreamContent%'
       OR COALESCE("SourceExcerpt", '') ILIKE '%FullPrompt%'
       OR COALESCE("SourceExcerpt", '') ILIKE '%SystemPrompt%'
       OR COALESCE("SourceExcerpt", '') ILIKE '%AttachmentContent%'
    UNION ALL
    SELECT COUNT(*)::bigint AS hits
    FROM "AppQuestionSources"
    WHERE LENGTH(COALESCE("SourceExcerpt", '')) > 512
       OR COALESCE("SourceExcerpt", '') ILIKE '%ContentText%'
       OR COALESCE("SourceExcerpt", '') ILIKE '%IRemoteStreamContent%'
       OR COALESCE("SourceExcerpt", '') ILIKE '%RemoteStreamContent%'
       OR COALESCE("SourceExcerpt", '') ILIKE '%FullPrompt%'
       OR COALESCE("SourceExcerpt", '') ILIKE '%SystemPrompt%'
       OR COALESCE("SourceExcerpt", '') ILIKE '%AttachmentContent%'
    UNION ALL
    SELECT COUNT(*)::bigint AS hits
    FROM "AbpAuditLogActions"
    WHERE (COALESCE("ServiceName", '') ILIKE '%KnowledgeFeedback%' OR COALESCE("MethodName", '') ILIKE '%Feedback%')
      AND (
          COALESCE("Parameters", '') ILIKE '%ContentText%'
          OR COALESCE("Parameters", '') ILIKE '%IRemoteStreamContent%'
          OR COALESCE("Parameters", '') ILIKE '%RemoteStreamContent%'
          OR COALESCE("Parameters", '') ILIKE '%AttachmentContent%'
      )
    UNION ALL
    SELECT COUNT(*)::bigint AS hits
    FROM "AppKnowledgeFeedbacks"
    WHERE LENGTH(COALESCE("Description", '')) > 2048
       OR LENGTH(COALESCE("Resolution", '')) > 2048
)
SELECT COALESCE(SUM(hits), 0) FROM hits;
"@

Write-Host "Recent audit actions"
Invoke-AuditSql $recentActionsSql

Write-Host "Operation summary"
Invoke-AuditSql $operationSummarySql

Write-Host "Sensitive audit field scan"
Invoke-AuditSql $sensitiveScanSql

Write-Host "Recent RAG calls"
Invoke-AuditSql $ragRecentCallsSql

Write-Host "RAG citation chain"
Invoke-AuditSql $ragCitationChainSql

Write-Host "RAG sensitive field scan"
Invoke-AuditSql $ragSensitiveScanSql

Write-Host "Recent AI question model calls"
Invoke-AuditSql $aiQuestionRecentCallsSql

Write-Host "Question review trail"
Invoke-AuditSql $questionReviewTrailSql

Write-Host "Knowledge feedback trail"
Invoke-AuditSql $knowledgeFeedbackTrailSql

Write-Host "Knowledge feedback audit actions"
Invoke-AuditSql $knowledgeFeedbackAuditActionsSql

Write-Host "Knowledge feedback sensitive field scan"
Invoke-AuditSql $knowledgeFeedbackSensitiveScanSql

Write-Host "Exam scoring closure"
Invoke-AuditSql $examScoringClosureSql

Write-Host "MVP-2 sensitive field scan"
Invoke-AuditSql $mvp2SensitiveScanSql

if ($FailOnSensitiveHits) {
    $sensitiveHits = [int64](Invoke-AuditScalar $sensitiveTotalSql)
    if ($sensitiveHits -gt 0) {
        throw "Sensitive audit field scan found $sensitiveHits hit(s)."
    }

    Write-Host "Sensitive audit field scan hard gate passed."
}
