[CmdletBinding()]
param(
    [string]$SolutionPath = ".\KnowPulse.slnx",
    [string]$Mvp1TestFilter = "FullyQualifiedName~Mvp1",
    [switch]$NoBuild,
    [switch]$WithBuild,
    [switch]$SkipDotnetTests,
    [switch]$SkipProviderBoundary,
    [switch]$SkipQdrant,
    [switch]$EnsureQdrant,
    [switch]$SkipLiteLlmConfig,
    [switch]$LiveLiteLlm,
    [switch]$SkipAudit,
    [switch]$AuditLocalPsql
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )

    [void]$results.Add([pscustomobject]@{
        Name = $Name
        Passed = $Passed
        Detail = $Detail
    })
}

function Invoke-VerifyStep {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host "==> $Name"
    try {
        $global:LASTEXITCODE = 0
        & $Action
        if ($LASTEXITCODE -ne 0) {
            throw "Command exited with code $LASTEXITCODE."
        }

        Add-Result -Name $Name -Passed $true -Detail "OK"
    }
    catch {
        Add-Result -Name $Name -Passed $false -Detail $_.Exception.Message
    }
}

Push-Location $repoRoot
try {
    if ($WithBuild) {
        Invoke-VerifyStep -Name "dotnet build" -Action {
            & dotnet build $SolutionPath --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false -p:UseAppHost=false
        }
    }

    if (-not $SkipDotnetTests) {
        Invoke-VerifyStep -Name "MVP-1 filtered tests" -Action {
            $testArgs = @(
                "test",
                $SolutionPath,
                "--no-restore",
                "--filter",
                $Mvp1TestFilter,
                "-v:m",
                "-m:1",
                "-nr:false",
                "-p:BuildInParallel=false"
            )

            if ($NoBuild) {
                $testArgs = @(
                    "test",
                    $SolutionPath,
                    "--no-build",
                    "--no-restore",
                    "--filter",
                    $Mvp1TestFilter,
                    "-v:m",
                    "-m:1",
                    "-nr:false"
                )
            }

            & dotnet @testArgs
        }
    }

    if (-not $SkipProviderBoundary) {
        Invoke-VerifyStep -Name "RAG provider boundary check" -Action {
            & .\ops\verify-rag-provider-boundaries.ps1
        }
    }

    if (-not $SkipQdrant) {
        Invoke-VerifyStep -Name "Qdrant knowledge collection check" -Action {
            $qdrantArgs = @{}
            if ($EnsureQdrant) {
                $qdrantArgs["Ensure"] = $true
            }

            & .\ops\verify-qdrant-knowledge-collection.ps1 @qdrantArgs
        }
    }

    if (-not $SkipLiteLlmConfig) {
        Invoke-VerifyStep -Name "LiteLLM RAG config check" -Action {
            $liteLlmArgs = @{}
            if ($LiveLiteLlm) {
                $liteLlmArgs["Live"] = $true
            }

            & .\ops\verify-litellm-rag-config.ps1 @liteLlmArgs
        }
    }

    if (-not $SkipAudit) {
        Invoke-VerifyStep -Name "RAG audit sensitive field check" -Action {
            $auditArgs = @{
                FailOnSensitiveHits = $true
            }
            if ($AuditLocalPsql) {
                $auditArgs["LocalPsql"] = $true
            }

            & .\ops\audit-check.ps1 @auditArgs
        }
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "MVP-1 verification summary"
$results | Format-Table -AutoSize

if ($results | Where-Object { -not $_.Passed }) {
    exit 1
}
