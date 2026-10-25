<#
.SYNOPSIS
验证 RAG provider 依赖没有泄漏到业务边界。

.DESCRIPTION
KnowPulse 业务层只能依赖项目内接口，不能在 Application/Domain 直接引用 Qdrant SDK、
Semantic Kernel Qdrant connector、VectorData preview 或 DataIngestion preview 包，
ADR 0003 约定的 MarkItDown 转换器及其两个直接依赖除外。
该脚本用于 CI 和本地审计，防止后续实现绕过项目契约。
#>

param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

# 业务层只能依赖项目内接口，如 IKnowledgeVectorStore / IKnowledgeIngestionPipeline。
# Qdrant SDK、VectorData provider 和 DataIngestion preview 包不得泄漏到 Application/Domain 边界。
$forbiddenPatterns = @(
    'using\s+Qdrant\.Client',
    'using\s+Microsoft\.SemanticKernel\.Connectors\.Qdrant',
    'using\s+Microsoft\.SemanticKernel',
    'using\s+Microsoft\.Extensions\.VectorData',
    'using\s+Microsoft\.Extensions\.DataIngestion',
    'using\s+Microsoft\.Extensions\.AI',
    'using\s+OpenAI',
    'using\s+Azure\.AI',
    '<PackageReference\s+Include="Qdrant\.Client"',
    '<PackageReference\s+Include="Microsoft\.SemanticKernel\.Connectors\.Qdrant"',
    '<PackageReference\s+Include="Microsoft\.SemanticKernel',
    '<PackageReference\s+Include="Microsoft\.Extensions\.VectorData',
    '<PackageReference\s+Include="Microsoft\.Extensions\.DataIngestion',
    '<PackageReference\s+Include="Microsoft\.Extensions\.AI',
    '<PackageReference\s+Include="OpenAI"',
    '<PackageReference\s+Include="Azure\.AI'
)

# ADR 0003 仅允许该转换器使用 DataIngestion，不能按目录或整个包名前缀放行。
$allowedStatements = @{
    'src/KnowPulse.Application/RagAI/KnowledgeDocumentMarkdownConverter.cs' = @(
        '(?m)^[ \t]*using\s+Microsoft\.Extensions\.DataIngestion;[ \t]*\r?$'
    )
    'src/KnowPulse.Application/KnowPulse.Application.csproj' = @(
        '(?m)^[ \t]*<PackageReference\s+Include="Microsoft\.Extensions\.DataIngestion\.(Abstractions|MarkItDown)"\s+Version="[^"]+"\s*/>[ \t]*\r?$'
    )
}

$scanRoots = @(
    Join-Path $Root 'src'
)

$files = Get-ChildItem -Path $scanRoots -Recurse -File -Include *.cs,*.csproj
$violations = @()
foreach ($file in $files) {
    # 每个文件逐模式扫描，输出具体文件和命中的禁用模式，便于快速定位越界引用。
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $relativePath = [System.IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
    foreach ($statement in $allowedStatements[$relativePath]) {
        $content = [regex]::Replace($content, $statement, '')
    }
    foreach ($pattern in $forbiddenPatterns) {
        if ($content -match $pattern) {
            $violations += [pscustomobject]@{
                File = $file.FullName
                Pattern = $pattern
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | Format-Table -AutoSize | Out-String | Write-Error
    exit 1
}

Write-Output 'RAG provider boundary check passed.'
