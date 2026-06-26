[CmdletBinding()]
param(
    [switch]$SkipDocker,
    [switch]$SkipHealthCheck,
    [switch]$SkipMigrator,
    [switch]$CoreOnly,
    [int]$HealthRetries = 10,
    [int]$HealthRetryDelaySeconds = 3,
    [string]$ConnectionString = "Host=localhost;Port=5432;Database=KnowPulse;User ID=root;Password=myPassword;",
    [string]$BlazorRootUrl = "https://localhost:44307",
    [string]$SwaggerRootUrl = "https://localhost:44305",
    [string]$DbMigratorProject = ".\src\KnowPulse.DbMigrator\KnowPulse.DbMigrator.csproj"
)

$ErrorActionPreference = "Stop"

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host "==> $Name"
    $global:LASTEXITCODE = 0
    & $Action

    $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode."
    }
}

if (-not $SkipDocker) {
    Invoke-Step "Starting Docker dependencies" {
        if ($CoreOnly) {
            docker compose up -d postgres minio
        }
        else {
            docker compose up -d
        }
    }
}

if (-not $SkipHealthCheck) {
    Invoke-Step "Checking dependency health" {
        if ($CoreOnly) {
            & .\ops\health-check.ps1 -CoreOnly -Retries $HealthRetries -RetryDelaySeconds $HealthRetryDelaySeconds
        }
        else {
            & .\ops\health-check.ps1 -Retries $HealthRetries -RetryDelaySeconds $HealthRetryDelaySeconds
        }
    }
}

if (-not $SkipMigrator) {
    Invoke-Step "Running DbMigrator" {
        $dbMigratorProjectPath = (Resolve-Path $DbMigratorProject).Path
        $dbMigratorProjectDirectory = Split-Path -Parent $dbMigratorProjectPath
        $dbMigratorProjectFile = Split-Path -Leaf $dbMigratorProjectPath

        $oldConnectionString = $env:ConnectionStrings__Default
        $oldBlazorClientId = $env:OpenIddict__Applications__KnowPulse_Blazor__ClientId
        $oldBlazorRootUrl = $env:OpenIddict__Applications__KnowPulse_Blazor__RootUrl
        $oldSwaggerClientId = $env:OpenIddict__Applications__KnowPulse_Swagger__ClientId
        $oldSwaggerRootUrl = $env:OpenIddict__Applications__KnowPulse_Swagger__RootUrl

        $env:ConnectionStrings__Default = $ConnectionString
        $env:OpenIddict__Applications__KnowPulse_Blazor__ClientId = "KnowPulse_Blazor"
        $env:OpenIddict__Applications__KnowPulse_Blazor__RootUrl = $BlazorRootUrl.TrimEnd("/")
        $env:OpenIddict__Applications__KnowPulse_Swagger__ClientId = "KnowPulse_Swagger"
        $env:OpenIddict__Applications__KnowPulse_Swagger__RootUrl = $SwaggerRootUrl.TrimEnd("/")

        Push-Location $dbMigratorProjectDirectory
        try {
            dotnet run --project $dbMigratorProjectFile
        }
        finally {
            Pop-Location
            $env:ConnectionStrings__Default = $oldConnectionString
            $env:OpenIddict__Applications__KnowPulse_Blazor__ClientId = $oldBlazorClientId
            $env:OpenIddict__Applications__KnowPulse_Blazor__RootUrl = $oldBlazorRootUrl
            $env:OpenIddict__Applications__KnowPulse_Swagger__ClientId = $oldSwaggerClientId
            $env:OpenIddict__Applications__KnowPulse_Swagger__RootUrl = $oldSwaggerRootUrl
        }
    }
}

if ($CoreOnly) {
    Write-Host "KnowPulse core local dependencies are ready."
}
else {
    Write-Host "KnowPulse local dependencies are ready."
}
