<#
.SYNOPSIS
验证 KnowPulse MVP-4 私有化部署配置基线。

.DESCRIPTION
默认执行仓库静态检查，不启动容器或应用。检查范围覆盖 Docker Compose 镜像 tag、
端口、数据卷、LiteLLM/Qdrant 密钥注入点、appsettings 环境变量覆盖入口，以及
真实密钥是否被误提交到 Git。

传入 -Environment Demo 或 -Environment Production 时，会额外检查当前进程环境变量中
是否已经提供演示/生产必需密钥，并拒绝继续使用本地开发默认值。
#>
[CmdletBinding()]
param(
    [ValidateSet("Development", "Demo", "Production")]
    [string]$Environment = "Development",
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$SkipDockerComposeConfig
)

$ErrorActionPreference = "Stop"

$results = [System.Collections.Generic.List[object]]::new()

function ConvertTo-DetailText {
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

function Add-CheckResult {
    param(
        [string]$Name,
        [bool]$Passed,
        [object]$Detail,
        [ValidateSet("Error", "Warning", "Info")]
        [string]$Severity = "Error"
    )

    $status = if ($Passed) {
        "PASS"
    }
    elseif ($Severity -eq "Warning") {
        "WARN"
    }
    else {
        "FAIL"
    }

    $results.Add([pscustomobject]@{
        Status = $status
        Check = $Name
        Detail = ConvertTo-DetailText $Detail
    }) | Out-Null
}

function Get-RepoPath {
    param([string]$RelativePath)

    return Join-Path $Root $RelativePath
}

function Read-TextFile {
    param([string]$RelativePath)

    $path = Get-RepoPath $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "File not found: $path"
    }

    return Get-Content -LiteralPath $path -Raw
}

function Read-JsonFile {
    param([string]$RelativePath)

    return Read-TextFile $RelativePath | ConvertFrom-Json
}

function Read-EnvExample {
    param([string]$RelativePath)

    $map = @{}
    $text = Read-TextFile $RelativePath
    foreach ($line in ($text -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf("=")
        if ($separatorIndex -lt 1) {
            continue
        }

        $name = $trimmed.Substring(0, $separatorIndex).Trim()
        $value = $trimmed.Substring($separatorIndex + 1).Trim()
        $map[$name] = $value.Trim('"').Trim("'")
    }

    return $map
}

function Test-TextContains {
    param(
        [string]$Name,
        [string]$Text,
        [string]$ExpectedText
    )

    Add-CheckResult `
        -Name $Name `
        -Passed ($Text -match [regex]::Escape($ExpectedText)) `
        -Detail $ExpectedText
}

function Get-ComposeImageReferences {
    param([string]$ComposeText)

    $items = [System.Collections.Generic.List[object]]::new()
    $inServices = $false
    $currentService = $null

    foreach ($line in ($ComposeText -split "`r?`n")) {
        if ($line -match '^services:\s*$') {
            $inServices = $true
            continue
        }

        if ($inServices -and $line -match '^\S' -and $line -notmatch '^services:\s*$') {
            $inServices = $false
        }

        if (-not $inServices) {
            continue
        }

        if ($line -match '^  (?<service>[A-Za-z0-9_-]+):\s*$') {
            $currentService = $Matches.service
            continue
        }

        if ($null -ne $currentService -and $line -match '^\s+image:\s*(?<image>.+?)\s*$') {
            $expression = $Matches.image.Trim().Trim('"').Trim("'")
            $variableName = $null
            $defaultImage = $expression

            if ($expression -match '^\$\{(?<variable>[A-Za-z0-9_]+):-(?<default>[^}]+)\}$') {
                $variableName = $Matches.variable
                $defaultImage = $Matches.default
            }

            $items.Add([pscustomobject]@{
                Service = $currentService
                VariableName = $variableName
                Expression = $expression
                DefaultImage = $defaultImage
            }) | Out-Null
        }
    }

    return $items
}

function Get-ImageTag {
    param([string]$Image)

    if ($Image -match '@sha256:[a-fA-F0-9]{64}') {
        return "<digest>"
    }

    $leaf = ($Image -split '/')[-1]
    if ($leaf -match ':(?<tag>[^:@]+)$') {
        return $Matches.tag
    }

    return ""
}

function Test-ImageReference {
    param(
        [string]$Service,
        [string]$Image
    )

    $tag = Get-ImageTag $Image
    $hasPinnedReference = -not [string]::IsNullOrWhiteSpace($tag)
    $usesLatest = $tag -eq "latest"
    $mutableChannelTags = @("main", "main-stable", "stable", "rc", "dev", "nightly")
    $usesMutableChannel = $mutableChannelTags -contains $tag -or $tag -match '(^|[-.])(rc|dev|nightly)($|[-.])'

    Add-CheckResult `
        -Name "$Service image has tag or digest" `
        -Passed $hasPinnedReference `
        -Detail $Image

    Add-CheckResult `
        -Name "$Service image does not use latest" `
        -Passed (-not $usesLatest) `
        -Detail $Image

    if ($Environment -ne "Development") {
        Add-CheckResult `
            -Name "$Service image avoids mutable channel tags for $Environment" `
            -Passed (-not $usesMutableChannel) `
            -Detail $Image
    }
}

function Test-EnvExampleValue {
    param(
        [hashtable]$EnvMap,
        [string]$Name,
        [AllowNull()]
        [object]$ExpectedValue = $null
    )

    $hasExpectedValue = $PSBoundParameters.ContainsKey("ExpectedValue")
    $exists = $EnvMap.ContainsKey($Name)
    $detail = if ($exists) { $EnvMap[$Name] } else { "missing" }
    Add-CheckResult -Name ".env.example defines $Name" -Passed $exists -Detail $detail

    if ($exists -and $hasExpectedValue) {
        Add-CheckResult `
            -Name ".env.example $Name matches baseline" `
            -Passed ($EnvMap[$Name] -eq "$ExpectedValue") `
            -Detail $EnvMap[$Name]
    }
}

function Test-RuntimeSecret {
    param(
        [string]$Name,
        [ValidateSet("Error", "Warning")]
        [string]$Severity = "Error"
    )

    $localOnlyValues = @(
        "myPassword",
        "knowpulse-local-password",
        "sk-local-dev-only",
        "local-dev-only-change-before-demo",
        "1q2w3E*",
        "mgigscLQCeNprVlp"
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    $hasValue = -not [string]::IsNullOrWhiteSpace($value)
    $looksLocalOnly = $hasValue -and (
        $localOnlyValues -contains $value -or
        $value -match 'local-dev|change-before-demo|myPassword|knowpulse-local-password'
    )

    Add-CheckResult `
        -Name "$Environment runtime secret $Name is set" `
        -Passed $hasValue `
        -Detail $(if ($hasValue) { "set" } else { "missing" }) `
        -Severity $Severity

    if ($hasValue) {
        Add-CheckResult `
            -Name "$Environment runtime secret $Name is not a development placeholder" `
            -Passed (-not $looksLocalOnly) `
            -Detail "value hidden" `
            -Severity $Severity
    }
}

$composeText = Read-TextFile "docker-compose.yml"
$envExample = Read-EnvExample ".env.example"
$liteLlmConfig = Read-TextFile "deploy\litellm\config.example.yaml"
$hostAppSettings = Read-JsonFile "src\KnowPulse.HttpApi.Host\appsettings.json"
$dbMigratorAppSettings = Read-JsonFile "src\KnowPulse.DbMigrator\appsettings.json"
$blazorClientAppSettings = Read-JsonFile "src\KnowPulse.Blazor.Client\wwwroot\appsettings.json"

$composeImages = @(Get-ComposeImageReferences $composeText)
Add-CheckResult -Name "docker-compose.yml defines expected service images" -Passed ($composeImages.Count -eq 4) -Detail (($composeImages | ForEach-Object { $_.Service }) -join ", ")

foreach ($image in $composeImages) {
    Test-ImageReference -Service $image.Service -Image $image.DefaultImage
    if (-not [string]::IsNullOrWhiteSpace($image.VariableName)) {
        Test-EnvExampleValue -EnvMap $envExample -Name $image.VariableName -ExpectedValue $image.DefaultImage
    }
}

$expectedPorts = [ordered]@{
    KNOWPULSE_POSTGRES_PORT = "5432"
    KNOWPULSE_MINIO_API_PORT = "9000"
    KNOWPULSE_MINIO_CONSOLE_PORT = "9001"
    KNOWPULSE_QDRANT_HTTP_PORT = "6333"
    KNOWPULSE_QDRANT_GRPC_PORT = "6334"
    KNOWPULSE_LITELLM_PORT = "4000"
}

foreach ($entry in $expectedPorts.GetEnumerator()) {
    Test-EnvExampleValue -EnvMap $envExample -Name $entry.Key -ExpectedValue $entry.Value
    Test-TextContains -Name "docker-compose.yml maps $($entry.Key)" -Text $composeText -ExpectedText ('${' + $entry.Key + ':-' + $entry.Value + '}')
}

$expectedVolumes = @(
    "knowpulse-postgres-data:/var/lib/postgresql",
    "knowpulse-minio-data:/data",
    "knowpulse-qdrant-data:/qdrant/storage"
)

foreach ($volume in $expectedVolumes) {
    Test-TextContains -Name "docker-compose.yml declares volume mount $volume" -Text $composeText -ExpectedText $volume
}

foreach ($volumeName in @("knowpulse-postgres-data:", "knowpulse-minio-data:", "knowpulse-qdrant-data:")) {
    Test-TextContains -Name "docker-compose.yml declares named volume $volumeName" -Text $composeText -ExpectedText $volumeName
}

foreach ($name in @(
    "POSTGRES_PASSWORD",
    "MINIO_ROOT_PASSWORD",
    "QDRANT__SERVICE__API_KEY",
    "KNOWPULSE_QDRANT_API_KEY",
    "LITELLM_MASTER_KEY",
    "KNOWPULSE_LITELLM_API_KEY",
    "DASHSCOPE_API_KEY",
    "OPENAI_API_KEY",
    "ConnectionStrings__Default",
    "BlobStoring__Minio__SecretKey",
    "RagAI__VectorStore__Endpoint",
    "RagAI__LiteLlm__BaseUrl",
    "App__SelfUrl",
    "App__CorsOrigins",
    "App__RedirectAllowedUrls",
    "AuthServer__Authority",
    "StringEncryption__DefaultPassPhrase"
)) {
    Test-EnvExampleValue -EnvMap $envExample -Name $name
}

Test-TextContains -Name "LiteLLM embedding key comes from environment" -Text $liteLlmConfig -ExpectedText "api_key: os.environ/DASHSCOPE_API_KEY"
Test-TextContains -Name "LiteLLM chat key comes from environment" -Text $liteLlmConfig -ExpectedText "api_key: os.environ/OPENAI_API_KEY"
Test-TextContains -Name "LiteLLM master key comes from environment" -Text $liteLlmConfig -ExpectedText "master_key: os.environ/LITELLM_MASTER_KEY"

Add-CheckResult `
    -Name "Host LiteLLM API key uses environment variable name" `
    -Passed ($hostAppSettings.RagAI.LiteLlm.ApiKeyEnvironmentVariable -eq "KNOWPULSE_LITELLM_API_KEY") `
    -Detail $hostAppSettings.RagAI.LiteLlm.ApiKeyEnvironmentVariable

Add-CheckResult `
    -Name "Host Qdrant API key uses environment variable name" `
    -Passed ($hostAppSettings.RagAI.VectorStore.ApiKeyEnvironmentVariable -eq "KNOWPULSE_QDRANT_API_KEY") `
    -Detail $hostAppSettings.RagAI.VectorStore.ApiKeyEnvironmentVariable

Add-CheckResult `
    -Name "DbMigrator LiteLLM API key uses environment variable name" `
    -Passed ($dbMigratorAppSettings.RagAI.LiteLlm.ApiKeyEnvironmentVariable -eq "KNOWPULSE_LITELLM_API_KEY") `
    -Detail $dbMigratorAppSettings.RagAI.LiteLlm.ApiKeyEnvironmentVariable

Add-CheckResult `
    -Name "DbMigrator Qdrant API key uses environment variable name" `
    -Passed ($dbMigratorAppSettings.RagAI.VectorStore.ApiKeyEnvironmentVariable -eq "KNOWPULSE_QDRANT_API_KEY") `
    -Detail $dbMigratorAppSettings.RagAI.VectorStore.ApiKeyEnvironmentVariable

Add-CheckResult `
    -Name "Host URL baseline is local API port" `
    -Passed ($hostAppSettings.App.SelfUrl -eq "https://localhost:44305") `
    -Detail $hostAppSettings.App.SelfUrl

Add-CheckResult `
    -Name "Host CORS includes local Blazor port" `
    -Passed ($hostAppSettings.App.CorsOrigins -match "https://localhost:44307") `
    -Detail $hostAppSettings.App.CorsOrigins

Add-CheckResult `
    -Name "Blazor client points to local API authority" `
    -Passed ($blazorClientAppSettings.AuthServer.Authority -eq "https://localhost:44305") `
    -Detail $blazorClientAppSettings.AuthServer.Authority

Add-CheckResult `
    -Name "Blazor client remote service points to local API" `
    -Passed ($blazorClientAppSettings.RemoteServices.Default.BaseUrl -eq "https://localhost:44305") `
    -Detail $blazorClientAppSettings.RemoteServices.Default.BaseUrl

$git = Get-Command git -ErrorAction SilentlyContinue
if ($null -ne $git) {
    $trackedFiles = @(& git -C $Root ls-files)
    $forbiddenTrackedFiles = @($trackedFiles | Where-Object {
        $_ -eq ".env" -or
        ($_ -like ".env.*" -and $_ -ne ".env.example") -or
        $_ -like "*appsettings.secrets.json" -or
        $_ -like "*.pfx"
    })

    Add-CheckResult `
        -Name "Git does not track local env or secret files" `
        -Passed ($forbiddenTrackedFiles.Count -eq 0) `
        -Detail $forbiddenTrackedFiles
}
else {
    Add-CheckResult -Name "Git secret-file tracking check" -Passed $false -Detail "git not found" -Severity Warning
}

if (-not $SkipDockerComposeConfig) {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -ne $docker) {
        $composePath = Get-RepoPath "docker-compose.yml"
        $envPath = Get-RepoPath ".env.example"
        $composeOutput = & docker compose --env-file $envPath -f $composePath config --quiet 2>&1
        Add-CheckResult `
            -Name "docker compose config parses with .env.example" `
            -Passed ($LASTEXITCODE -eq 0) `
            -Detail $composeOutput
    }
    else {
        Add-CheckResult -Name "docker compose config parses with .env.example" -Passed $false -Detail "docker not found" -Severity Warning
    }
}

if ($Environment -ne "Development") {
    foreach ($name in @(
        "POSTGRES_PASSWORD",
        "MINIO_ROOT_PASSWORD",
        "LITELLM_MASTER_KEY",
        "KNOWPULSE_LITELLM_API_KEY",
        "DASHSCOPE_API_KEY",
        "OPENAI_API_KEY",
        "ConnectionStrings__Default",
        "BlobStoring__Minio__SecretKey",
        "StringEncryption__DefaultPassPhrase"
    )) {
        Test-RuntimeSecret -Name $name
    }

    $qdrantSecretSeverity = if ($Environment -eq "Production") { "Error" } else { "Warning" }
    foreach ($name in @("QDRANT__SERVICE__API_KEY", "KNOWPULSE_QDRANT_API_KEY")) {
        Test-RuntimeSecret -Name $name -Severity $qdrantSecretSeverity
    }
}

$results | Format-Table -AutoSize

if ($results | Where-Object { $_.Status -eq "FAIL" }) {
    exit 1
}

Write-Output "KnowPulse private deployment baseline check passed for $Environment."
