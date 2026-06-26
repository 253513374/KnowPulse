[CmdletBinding()]
param(
    [string]$PostgresHost = "localhost",
    [int]$PostgresPort = 5432,
    [string]$MinioHealthUrl = "http://localhost:9000/minio/health/live",
    [string]$QdrantHealthUrl = "http://localhost:6333/healthz",
    [string[]]$LiteLlmHealthUrls = @("http://localhost:4000/health/liveliness", "http://localhost:4000/health"),
    [switch]$CoreOnly,
    [int]$Retries = 1,
    [int]$RetryDelaySeconds = 3
)

$ErrorActionPreference = "Stop"

function Test-TcpPort {
    param(
        [string]$Name,
        [string]$HostName,
        [int]$Port
    )

    $client = [System.Net.Sockets.TcpClient]::new()

    try {
        $connect = $client.BeginConnect($HostName, $Port, $null, $null)
        $ok = $connect.AsyncWaitHandle.WaitOne([TimeSpan]::FromSeconds(3))

        if (-not $ok) {
            return [pscustomobject]@{ Name = $Name; Ok = $false; Detail = "Timeout connecting to ${HostName}:${Port}" }
        }

        $client.EndConnect($connect)
        return [pscustomobject]@{ Name = $Name; Ok = $true; Detail = "${HostName}:${Port}" }
    }
    catch {
        return [pscustomobject]@{ Name = $Name; Ok = $false; Detail = $_.Exception.Message }
    }
    finally {
        $client.Dispose()
    }
}

function Test-HttpEndpoint {
    param(
        [string]$Name,
        [string[]]$Urls
    )

    foreach ($url in $Urls) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return [pscustomobject]@{ Name = $Name; Ok = $true; Detail = "$url -> HTTP $($response.StatusCode)" }
            }
        }
        catch {
            $lastError = $_.Exception.Message
        }
    }

    return [pscustomobject]@{ Name = $Name; Ok = $false; Detail = $lastError }
}

function Invoke-HealthChecks {
    $results = @(
        Test-TcpPort -Name "PostgreSQL" -HostName $PostgresHost -Port $PostgresPort
        Test-HttpEndpoint -Name "MinIO" -Urls @($MinioHealthUrl)
    )

    if (-not $CoreOnly) {
        $results += @(
            Test-HttpEndpoint -Name "Qdrant" -Urls @($QdrantHealthUrl)
            Test-HttpEndpoint -Name "LiteLLM" -Urls $LiteLlmHealthUrls
        )
    }

    return $results
}

if ($Retries -lt 1) {
    throw "Retries must be at least 1."
}

for ($attempt = 1; $attempt -le $Retries; $attempt++) {
    $results = Invoke-HealthChecks
    if (-not ($results | Where-Object { -not $_.Ok })) {
        break
    }

    if ($attempt -lt $Retries) {
        Start-Sleep -Seconds $RetryDelaySeconds
    }
}

$results | Format-Table -AutoSize

if ($results | Where-Object { -not $_.Ok }) {
    exit 1
}
