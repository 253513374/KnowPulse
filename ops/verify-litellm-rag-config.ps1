<#
.SYNOPSIS
验证 KnowPulse RAG 使用的 LiteLLM alias、环境变量和可选运行时网关状态。

.DESCRIPTION
默认执行仓库静态检查，确保 appsettings、.env.example 和 deploy/litellm/config.example.yaml
中的 embedding/chat alias 保持一致。传入 -Live 时会额外访问本地 LiteLLM 网关，确认运行中
的 /health 和 /v1/models 能看到预期 alias。
#>
[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$LiteLlmBaseUrl = "http://localhost:4000",
    [switch]$Live
)

$ErrorActionPreference = "Stop"

$expectedEmbeddingModel = "knowpulse-embedding-default"
$expectedFallbackModel = "knowpulse-embedding-fallback"
$expectedChatModel = "knowpulse-chat-default"
$expectedApiKeyVariable = "KNOWPULSE_LITELLM_API_KEY"
$expectedDeepSeekApiKeyVariable = "DEEPSEEK_API_KEY"

# 该脚本分两层验证：
# - 默认模式只做仓库静态检查，确保 appsettings、.env.example 和 LiteLLM 模板使用同一组 alias。
# - -Live 模式再访问本地 LiteLLM，确认运行中的网关已经加载这些 alias。
# 将检查详情压缩为适合表格输出的短文本，避免长数组或长 JSON 让失败摘要失焦。
function ConvertTo-DetailText {
    # 将数组或长文本压缩成适合表格展示的短 detail，避免失败输出刷屏。
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    if ($Value -is [System.Array]) {
        $text = (@($Value) | ForEach-Object { "$_" }) -join ", "
    }
    else {
        $text = "$Value"
    }

    if ($text.Length -gt 300) {
        return "$($text.Substring(0, 300))..."
    }

    return $text
}

# 向统一结果列表追加一条检查结果，并提前归一化详情文本。
function Add-CheckResult {
    # 所有检查统一写入 Results，最后一次性表格输出并按失败项决定退出码。
    param(
        [System.Collections.Generic.List[object]]$Results,
        [string]$Name,
        [bool]$Passed,
        [object]$Detail
    )

    $Results.Add([pscustomobject]@{
        Name = $Name
        Passed = $Passed
        Detail = (ConvertTo-DetailText $Detail)
    }) | Out-Null
}

# 读取并解析 JSON 配置文件，缺文件时直接抛出可定位的路径错误。
function Read-JsonFile {
    # ConvertFrom-Json 能保留 appsettings 层级结构，比正则匹配 JSON 更稳。
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "File not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

# 校验指定 appsettings 文件的 RagAI 配置节和 LiteLLM alias 是否符合约定。
function Test-AppSettings {
    # 检查应用运行配置中的 RagAI 节，保证 HttpApi.Host 和 DbMigrator 使用同一套 alias。
    param(
        [System.Collections.Generic.List[object]]$Results,
        [string]$RelativePath
    )

    $path = Join-Path $Root $RelativePath
    $json = Read-JsonFile $path
    $ragAi = $json.RagAI
    $liteLlm = if ($null -ne $ragAi) { $ragAi.LiteLlm } else { $null }
    $embedding = if ($null -ne $ragAi) { $ragAi.Embedding } else { $null }

    # 先校验配置节存在，再读取具体属性，避免缺节时 PowerShell 把对象展开成大量噪声输出。
    Add-CheckResult $Results "$RelativePath has RagAI:LiteLlm" ($null -ne $liteLlm) "LiteLlm config section"
    Add-CheckResult $Results "$RelativePath has RagAI:Embedding" ($null -ne $embedding) "Embedding config section"
    if ($null -eq $liteLlm -or $null -eq $embedding) {
        return
    }

    Add-CheckResult $Results "$RelativePath embedding alias" ($embedding.ModelName -eq $expectedEmbeddingModel) $embedding.ModelName
    Add-CheckResult $Results "$RelativePath chat alias" ($liteLlm.ChatModel -eq $expectedChatModel) $liteLlm.ChatModel
    Add-CheckResult $Results "$RelativePath gateway embedding alias" ($liteLlm.EmbeddingModel -eq $expectedEmbeddingModel) $liteLlm.EmbeddingModel
    Add-CheckResult $Results "$RelativePath fallback alias" (@($liteLlm.FallbackModels) -contains $expectedFallbackModel) (@($liteLlm.FallbackModels) -join ", ")
    Add-CheckResult $Results "$RelativePath API key variable" ($liteLlm.ApiKeyEnvironmentVariable -eq $expectedApiKeyVariable) $liteLlm.ApiKeyEnvironmentVariable
}

# 对文本模板执行精确片段检查，用于验证 YAML 和 .env.example 中的关键配置。
function Test-TextContains {
    # 对 YAML 和 .env.example 这类文本模板做精确片段检查，避免引入 YAML parser 依赖。
    param(
        [System.Collections.Generic.List[object]]$Results,
        [string]$Name,
        [string]$Text,
        [string]$Pattern
    )

    Add-CheckResult $Results $Name ($Text -match [regex]::Escape($Pattern)) $Pattern
}

# 访问本地 LiteLLM 网关的健康和模型列表端点，验证运行时 alias 是否已加载。
function Invoke-LiteLlmLiveCheck {
    # Live 检查只验证本地网关连通性和 alias 是否加载，不发起真实 embedding/chat 请求。
    param([System.Collections.Generic.List[object]]$Results)

    $apiKey = [Environment]::GetEnvironmentVariable($expectedApiKeyVariable)
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        # 本地 docker-compose 默认使用 LITELLM_MASTER_KEY；应用运行时优先使用 KNOWPULSE_LITELLM_API_KEY。
        $apiKey = [Environment]::GetEnvironmentVariable("LITELLM_MASTER_KEY")
    }

    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace($apiKey)) {
        $headers["Authorization"] = "Bearer $apiKey"
    }

    $baseUrl = $LiteLlmBaseUrl.TrimEnd("/")
    $healthUrls = @("$baseUrl/health/liveliness", "$baseUrl/health")
    $healthPassed = $false
    $healthDetail = ""
    foreach ($url in $healthUrls) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -Headers $headers -TimeoutSec 10
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                $healthPassed = $true
                $healthDetail = "$url -> HTTP $($response.StatusCode)"
                break
            }
        }
        catch {
            $healthDetail = $_.Exception.Message
        }
    }

    Add-CheckResult $Results "LiteLLM health endpoint" $healthPassed $healthDetail

    try {
        $modelsResponse = Invoke-RestMethod -Method Get -Uri "$baseUrl/v1/models" -Headers $headers -TimeoutSec 10
        $modelIds = @($modelsResponse.data | ForEach-Object { $_.id })
        $allAliases = @($expectedEmbeddingModel, $expectedFallbackModel, $expectedChatModel)
        foreach ($alias in $allAliases) {
            Add-CheckResult $Results "LiteLLM model alias $alias" ($modelIds -contains $alias) ($modelIds -join ", ")
        }
    }
    catch {
        Add-CheckResult $Results "LiteLLM model list endpoint" $false $_.Exception.Message
    }
}

$results = [System.Collections.Generic.List[object]]::new()

Test-AppSettings $results "src\KnowPulse.HttpApi.Host\appsettings.json"
Test-AppSettings $results "src\KnowPulse.DbMigrator\appsettings.json"

$configPath = Join-Path $Root "deploy\litellm\config.example.yaml"
$configText = Get-Content -LiteralPath $configPath -Raw
Test-TextContains $results "LiteLLM template embedding alias" $configText "model_name: $expectedEmbeddingModel"
Test-TextContains $results "LiteLLM template fallback alias" $configText "model_name: $expectedFallbackModel"
Test-TextContains $results "LiteLLM template chat alias" $configText "model_name: $expectedChatModel"
Test-TextContains $results "LiteLLM template uses OPENAI_API_KEY" $configText "api_key: os.environ/OPENAI_API_KEY"
Test-TextContains $results "LiteLLM template routes chat to DeepSeek" $configText "model: deepseek/deepseek-chat"
Test-TextContains $results "LiteLLM template uses DEEPSEEK_API_KEY" $configText "api_key: os.environ/$expectedDeepSeekApiKeyVariable"

$envExamplePath = Join-Path $Root ".env.example"
$envExampleText = Get-Content -LiteralPath $envExamplePath -Raw
Test-TextContains $results ".env.example app LiteLLM API key" $envExampleText "$expectedApiKeyVariable="
Test-TextContains $results ".env.example LiteLLM master key" $envExampleText "LITELLM_MASTER_KEY="
Test-TextContains $results ".env.example upstream model key" $envExampleText "OPENAI_API_KEY="
Test-TextContains $results ".env.example DeepSeek model key" $envExampleText "$expectedDeepSeekApiKeyVariable="

if ($Live) {
    Invoke-LiteLlmLiveCheck $results
}

$results | Format-Table -AutoSize

if ($results | Where-Object { -not $_.Passed }) {
    exit 1
}

Write-Output "LiteLLM RAG config check passed."
