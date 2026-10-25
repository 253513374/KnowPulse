<#
.SYNOPSIS
检查或初始化 KnowPulse MVP-1 知识向量 Qdrant collection。

.DESCRIPTION
默认只读取 collection 健康状态并校验向量维度和必需 payload index。
传入 -Ensure 时会创建缺失的 collection 和 payload index，但不会删除或重写已有 point 数据。
#>
[CmdletBinding()]
param(
    [string]$QdrantEndpoint = "http://localhost:6333",
    [string]$CollectionName = "knowledge_chunks_qwen_v4_1536",
    [int]$VectorDimension = 1536,
    [ValidateSet("Cosine", "Dot", "Euclid", "Manhattan")]
    [string]$Distance = "Cosine",
    [switch]$Ensure,
    [string]$ApiKeyEnvironmentVariable = "KNOWPULSE_QDRANT_API_KEY"
)

$ErrorActionPreference = "Stop"

# MVP-1 的 Qdrant collection 是可重建索引，不是业务主库。
# 这些 payload index 只用于检索前粗过滤；最终权限仍由 PostgreSQL 二次校验决定。
$requiredPayloadIndexes = [ordered]@{
    tenantId = "keyword"
    knowledgeItemId = "keyword"
    knowledgeVersionId = "keyword"
    chunkId = "keyword"
    isPublished = "bool"
    departmentIds = "keyword"
    positionIds = "keyword"
    roleIds = "keyword"
    userIds = "keyword"
    securityLevel = "integer"
}

# 构造 Qdrant REST 调用需要的请求头，按需从环境变量读取 api-key。
function New-QdrantHeaders {
    # Qdrant Cloud 或受保护环境可通过环境变量传 api-key；本地开发为空时按无认证访问。
    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace($ApiKeyEnvironmentVariable)) {
        $apiKey = [Environment]::GetEnvironmentVariable($ApiKeyEnvironmentVariable)
        if (-not [string]::IsNullOrWhiteSpace($apiKey)) {
            $headers["api-key"] = $apiKey
        }
    }

    return $headers
}

# 拼接 Qdrant endpoint 和 REST 相对路径，统一处理首尾斜杠。
function Join-QdrantUrl {
    # 统一拼接 endpoint 和相对路径，避免调用点重复处理斜杠。
    param([string]$Path)

    $baseUrl = $QdrantEndpoint.TrimEnd("/")
    $relativePath = $Path.TrimStart("/")
    return "$baseUrl/$relativePath"
}

$script:QdrantHeaders = New-QdrantHeaders
$script:EscapedCollectionName = [Uri]::EscapeDataString($CollectionName)

# 执行 Qdrant REST 请求，并允许调用方把指定 HTTP 错误码视为可预期结果。
function Invoke-QdrantRequest {
    # 对 Qdrant REST 调用做轻量封装，允许健康检查场景把 404/409 当作可预期结果处理。
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [int[]]$AllowedErrorStatusCodes = @()
    )

    $parameters = @{
        Method = $Method
        Uri = (Join-QdrantUrl -Path $Path)
        Headers = $script:QdrantHeaders
        TimeoutSec = 15
    }

    if ($null -ne $Body) {
        $parameters["ContentType"] = "application/json"
        $parameters["Body"] = ($Body | ConvertTo-Json -Depth 20)
    }

    try {
        return Invoke-RestMethod @parameters
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }

        if ($null -ne $statusCode -and ($AllowedErrorStatusCodes -contains $statusCode)) {
            return $null
        }

        throw
    }
}

# 读取目标 Qdrant collection，并兼容 result 包装和直接返回对象两种响应形态。
function Get-QdrantCollection {
    # Qdrant REST 响应通常包在 result 下；测试和不同版本响应可能直接返回对象，因此两种都兼容。
    $response = Invoke-QdrantRequest `
        -Method "Get" `
        -Path "collections/$script:EscapedCollectionName" `
        -AllowedErrorStatusCodes @(404)

    if ($null -eq $response) {
        return $null
    }

    if ($null -ne $response.result) {
        return $response.result
    }

    return $response
}

# 创建缺失的 Qdrant collection，只设置向量维度和距离算法。
function New-QdrantCollection {
    # 创建 collection 只设置向量维度和距离算法，payload index 后续逐个创建。
    $body = @{
        vectors = @{
            size = $VectorDimension
            distance = $Distance
        }
    }

    Invoke-QdrantRequest `
        -Method "Put" `
        -Path "collections/$script:EscapedCollectionName" `
        -Body $body | Out-Null
}

# 为指定 payload 字段创建索引，并将并发创建导致的 409 视为幂等成功。
function New-QdrantPayloadIndex {
    # 创建单个 payload index；409 表示其他进程已经创建，视为幂等成功。
    param(
        [string]$FieldName,
        [string]$FieldSchema
    )

    $body = @{
        field_name = $FieldName
        field_schema = $FieldSchema
    }

    Invoke-QdrantRequest `
        -Method "Put" `
        -Path "collections/$script:EscapedCollectionName/index?wait=true" `
        -Body $body `
        -AllowedErrorStatusCodes @(409) | Out-Null
}

# 从 Qdrant collection 配置中提取向量维度，兼容 unnamed vector 和 named vector。
function Get-VectorDimension {
    # 同时兼容 unnamed vector 和 named vector 的 Qdrant collection 配置结构。
    param([object]$Collection)

    if ($null -eq $Collection -or
        $null -eq $Collection.config -or
        $null -eq $Collection.config.params -or
        $null -eq $Collection.config.params.vectors) {
        return $null
    }

    $vectors = $Collection.config.params.vectors
    $sizeProperty = @($vectors.PSObject.Properties) | Where-Object { $_.Name -eq "size" } | Select-Object -First 1
    if ($null -ne $sizeProperty) {
        return [int]$sizeProperty.Value
    }

    foreach ($property in @($vectors.PSObject.Properties)) {
        if ($null -eq $property.Value) {
            continue
        }

        $namedSizeProperty = @($property.Value.PSObject.Properties) | Where-Object { $_.Name -eq "size" } | Select-Object -First 1
        if ($null -ne $namedSizeProperty) {
            return [int]$namedSizeProperty.Value
        }
    }

    return $null
}

# 从 Qdrant collection 的 payload_schema 中读取已经创建索引的字段名集合。
function Get-PayloadSchemaFields {
    # payload_schema 的属性名就是已经创建索引的字段名。
    param([object]$Collection)

    if ($null -eq $Collection -or $null -eq $Collection.payload_schema) {
        return @()
    }

    return @($Collection.payload_schema.PSObject.Properties | ForEach-Object { $_.Name })
}

# 计算 MVP-1 必需 payload index 中尚未存在的字段名集合。
function Get-MissingPayloadIndexes {
    # 用 HashSet 做精确大小写匹配，避免字段名大小写漂移被误判为健康。
    param([string[]]$ExistingFields)

    $existingSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($field in $ExistingFields) {
        [void]$existingSet.Add($field)
    }

    $missing = @()
    foreach ($fieldName in $requiredPayloadIndexes.Keys) {
        if (-not $existingSet.Contains($fieldName)) {
            $missing += $fieldName
        }
    }

    return $missing
}

$collection = Get-QdrantCollection
if ($null -eq $collection) {
    if (-not $Ensure) {
        [pscustomobject]@{
            CollectionName = $CollectionName
            CollectionExists = $false
            ExpectedVectorDimension = $VectorDimension
            ActualVectorDimension = $null
            VectorDimensionMatches = $false
            MissingPayloadIndexes = ($requiredPayloadIndexes.Keys -join ", ")
        } | Format-List

        Write-Error "Qdrant collection '$CollectionName' does not exist. Re-run with -Ensure to create it." -ErrorAction Continue
        exit 1
    }

    # -Ensure 只创建缺失的 collection/index，不删除现有点位数据，适合本地环境反复执行。
    Write-Output "Creating Qdrant collection '$CollectionName' with vector dimension $VectorDimension..."
    New-QdrantCollection
    $collection = Get-QdrantCollection
}

if ($null -eq $collection) {
    Write-Error "Qdrant collection '$CollectionName' is still missing after creation." -ErrorAction Continue
    exit 1
}

$existingFields = Get-PayloadSchemaFields -Collection $collection
$missingPayloadIndexes = Get-MissingPayloadIndexes -ExistingFields $existingFields

if ($Ensure -and $missingPayloadIndexes.Count -gt 0) {
    foreach ($fieldName in $missingPayloadIndexes) {
        $fieldSchema = $requiredPayloadIndexes[$fieldName]
        # Qdrant payload index 创建可能已经由其他进程完成，409 被视为可接受的幂等结果。
        Write-Output "Creating Qdrant payload index '$fieldName' ($fieldSchema)..."
        New-QdrantPayloadIndex -FieldName $fieldName -FieldSchema $fieldSchema
    }

    $collection = Get-QdrantCollection
    $existingFields = Get-PayloadSchemaFields -Collection $collection
    $missingPayloadIndexes = Get-MissingPayloadIndexes -ExistingFields $existingFields
}

$actualVectorDimension = Get-VectorDimension -Collection $collection
$vectorDimensionMatches = $actualVectorDimension -eq $VectorDimension

[pscustomobject]@{
    CollectionName = $CollectionName
    CollectionExists = $true
    ExpectedVectorDimension = $VectorDimension
    ActualVectorDimension = $actualVectorDimension
    VectorDimensionMatches = $vectorDimensionMatches
    RequiredPayloadIndexes = ($requiredPayloadIndexes.Keys -join ", ")
    ExistingPayloadIndexes = ($existingFields -join ", ")
    MissingPayloadIndexes = ($missingPayloadIndexes -join ", ")
} | Format-List

if (-not $vectorDimensionMatches -or $missingPayloadIndexes.Count -gt 0) {
    Write-Error "Qdrant collection '$CollectionName' is not healthy." -ErrorAction Continue
    exit 1
}

Write-Output "Qdrant knowledge collection is healthy."
