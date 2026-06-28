[CmdletBinding()]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repoRoot
try {
    $domainArgs = @(
        "test",
        ".\test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj",
        "--filter",
        "FullyQualifiedName~RagEvaluationTests",
        "--no-restore",
        "-m:1",
        "-p:UseSharedCompilation=false",
        "-p:BuildInParallel=false",
        "-nr:false"
    )

    $appArgs = @(
        "test",
        ".\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj",
        "--filter",
        "FullyQualifiedName~Mvp1RagEvaluationAppServiceTests",
        "--no-restore",
        "-m:1",
        "-p:UseSharedCompilation=false",
        "-p:BuildInParallel=false",
        "-nr:false"
    )

    $apiArgs = @(
        "test",
        ".\test\KnowPulse.Application.Tests\KnowPulse.Application.Tests.csproj",
        "--filter",
        "FullyQualifiedName~RagEvaluationApiContractTests",
        "--no-restore",
        "-m:1",
        "-p:UseSharedCompilation=false",
        "-p:BuildInParallel=false",
        "-nr:false"
    )

    if ($NoBuild) {
        $domainArgs = $domainArgs + "--no-build"
        $appArgs = $appArgs + "--no-build"
        $apiArgs = $apiArgs + "--no-build"
    }

    Write-Host "==> RAG evaluation domain tests"
    & dotnet @domainArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Write-Host "==> RAG evaluation application service tests"
    & dotnet @appArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Write-Host "==> RAG evaluation API contract tests"
    & dotnet @apiArgs
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
