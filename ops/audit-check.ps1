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
    [switch]$LocalPsql
)

$ErrorActionPreference = "Stop"

if ($Limit -lt 1 -or $Limit -gt 500) {
    throw "Limit must be between 1 and 500."
}

function ConvertTo-SqlLiteral {
    param([string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

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

Write-Host "Recent audit actions"
Invoke-AuditSql $recentActionsSql

Write-Host "Operation summary"
Invoke-AuditSql $operationSummarySql

Write-Host "Sensitive audit field scan"
Invoke-AuditSql $sensitiveScanSql
