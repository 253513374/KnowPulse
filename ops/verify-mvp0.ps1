[CmdletBinding()]
param(
    [string]$ApiBaseUrl = "https://localhost:44305",
    [string]$BlazorBaseUrl = "https://localhost:44307",
    [switch]$SkipHealth,
    [switch]$SkipStatic,
    [switch]$SkipAudit,
    [switch]$SkipEndpoints,
    [switch]$SkipDotnetTests,
    [switch]$WithBuild,
    [switch]$WithRuntime,
    [switch]$WithDemoSeed,
    [switch]$WithPageAttachmentPreflight,
    [string]$RuntimeAdminUserName = "admin",
    [string]$RuntimeAdminPassword = "1q2w3E*"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )

    $results.Add([pscustomobject]@{
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

function Test-HttpStatus {
    param(
        [string]$Name,
        [string]$Url,
        [int[]]$ExpectedStatusCodes
    )

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl -ne $null) {
        $tempOutput = [System.IO.Path]::GetTempFileName()
        try {
            $global:LASTEXITCODE = 0
            $statusText = & curl.exe -k -s -o $tempOutput -w "%{http_code}" --max-time 10 $Url
            if ($LASTEXITCODE -ne 0) {
                throw "curl.exe exited with code $LASTEXITCODE for $Url."
            }

            $statusCode = [int]$statusText
            if ($ExpectedStatusCodes -notcontains $statusCode) {
                throw "$Url returned HTTP $statusCode; expected one of $($ExpectedStatusCodes -join ', ')."
            }

            Write-Host "$Name -> HTTP $statusCode"
            return
        }
        finally {
            Remove-Item -LiteralPath $tempOutput -Force -ErrorAction SilentlyContinue
        }
    }

    $previousCertificateCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

    try {
        $request = [System.Net.WebRequest]::Create($Url)
        $request.AllowAutoRedirect = $false
        $request.Timeout = 10000

        try {
            $response = $request.GetResponse()
        }
        catch [System.Net.WebException] {
            if ($_.Exception.Response -eq $null) {
                throw
            }

            $response = $_.Exception.Response
        }

        $statusCode = [int]$response.StatusCode
        if ($ExpectedStatusCodes -notcontains $statusCode) {
            throw "$Url returned HTTP $statusCode; expected one of $($ExpectedStatusCodes -join ', ')."
        }

        Write-Host "$Name -> HTTP $statusCode"
    }
    finally {
        if ($response -ne $null) {
            $response.Dispose()
        }

        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCertificateCallback
    }
}

Push-Location $repoRoot
try {
    if ($WithBuild) {
        Invoke-VerifyStep -Name "dotnet build" -Action {
            & dotnet build .\KnowPulse.slnx --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false -p:UseAppHost=false
        }
    }

    if (-not $SkipDotnetTests) {
        Invoke-VerifyStep -Name "dotnet test" -Action {
            & dotnet test .\KnowPulse.slnx --no-build --no-restore -v:m -m:1 -nr:false
        }
    }

    if (-not $SkipHealth) {
        Invoke-VerifyStep -Name "dependency health check" -Action {
            & .\ops\health-check.ps1 -Retries 3 -RetryDelaySeconds 2
        }
    }

    if (-not $SkipStatic) {
        Invoke-VerifyStep -Name "MVP-0 static contract check" -Action {
            & .\ops\verify-mvp0-static.ps1
        }
    }

    if (-not $SkipEndpoints) {
        Invoke-VerifyStep -Name "local endpoint check" -Action {
            Test-HttpStatus -Name "OpenID configuration" -Url "$ApiBaseUrl/.well-known/openid-configuration" -ExpectedStatusCodes @(200)
            Test-HttpStatus -Name "Swagger UI" -Url "$ApiBaseUrl/swagger/index.html" -ExpectedStatusCodes @(200)
            Test-HttpStatus -Name "Swagger JSON" -Url "$ApiBaseUrl/swagger/v1/swagger.json" -ExpectedStatusCodes @(200)
            Test-HttpStatus -Name "Blazor shell" -Url "$BlazorBaseUrl/" -ExpectedStatusCodes @(200)
            Test-HttpStatus -Name "Blazor dotnet runtime" -Url "$BlazorBaseUrl/_framework/dotnet.js" -ExpectedStatusCodes @(200)
            Test-HttpStatus -Name "Unauthenticated department API" -Url "$ApiBaseUrl/api/app/department" -ExpectedStatusCodes @(302, 401, 403)
            Test-HttpStatus -Name "Unauthenticated position API" -Url "$ApiBaseUrl/api/app/position" -ExpectedStatusCodes @(302, 401, 403)
            Test-HttpStatus -Name "Unauthenticated knowledge category API" -Url "$ApiBaseUrl/api/app/knowledge-category" -ExpectedStatusCodes @(302, 401, 403)
            Test-HttpStatus -Name "Unauthenticated knowledge item API" -Url "$ApiBaseUrl/api/app/knowledge-item" -ExpectedStatusCodes @(302, 401, 403)
        }
    }

    if ($WithRuntime) {
        Invoke-VerifyStep -Name "MVP-0 authenticated runtime flow" -Action {
            & .\ops\verify-mvp0-runtime.ps1 `
                -ApiBaseUrl $ApiBaseUrl `
                -AdminUserName $RuntimeAdminUserName `
                -AdminPassword $RuntimeAdminPassword
        }
    }

    if ($WithDemoSeed) {
        Invoke-VerifyStep -Name "MVP-0 demo seed data" -Action {
            & .\ops\seed-demo-data.ps1 `
                -ApiBaseUrl $ApiBaseUrl `
                -AdminUserName $RuntimeAdminUserName `
                -AdminPassword $RuntimeAdminPassword
        }
    }

    if ($WithPageAttachmentPreflight) {
        Invoke-VerifyStep -Name "MVP-0 page attachment preflight" -Action {
            & .\ops\check-page-attachment-prereqs.ps1 `
                -ApiBaseUrl $ApiBaseUrl `
                -BlazorBaseUrl $BlazorBaseUrl `
                -AdminUserName $RuntimeAdminUserName `
                -AdminPassword $RuntimeAdminPassword
        }
    }

    if (-not $SkipAudit) {
        Invoke-VerifyStep -Name "audit log check" -Action {
            & .\ops\audit-check.ps1
        }
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "MVP-0 verification summary"
$results | Format-Table -AutoSize

if ($results | Where-Object { -not $_.Passed }) {
    exit 1
}
