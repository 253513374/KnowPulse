[CmdletBinding()]
param(
    [string]$ApiBaseUrl = "https://localhost:44305",
    [string]$AdminUserName = "admin",
    [string]$AdminPassword = "1q2w3E*",
    [string]$SeedPrefix = "MVP2-DEMO",
    [string]$RunTag = "",
    [int]$DurationMinutes = 30
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("knowpulse-mvp2-demo-seed-" + [Guid]::NewGuid().ToString("N"))
$cookiePath = Join-Path $workDir "cookies.txt"
$loginPagePath = Join-Path $workDir "login.html"
$responsePath = Join-Path $workDir "response.json"

function Invoke-Curl {
    param(
        [string[]]$Arguments,
        [int[]]$ExpectedStatusCodes,
        [string]$Name
    )

    $global:LASTEXITCODE = 0
    $statusCodeText = & curl.exe @Arguments -w "%{http_code}"
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed: curl.exe exited with code $LASTEXITCODE."
    }

    $statusCode = [int]$statusCodeText
    if ($ExpectedStatusCodes -notcontains $statusCode) {
        $body = if (Test-Path -LiteralPath $responsePath) { Get-Content -LiteralPath $responsePath -Raw } else { "" }
        throw "$Name returned HTTP $statusCode; expected $($ExpectedStatusCodes -join ', '). Body: $body"
    }

    return $statusCode
}

function Get-XsrfToken {
    $line = Get-Content -LiteralPath $cookiePath |
        Where-Object { $_ -and -not $_.StartsWith("#") -and ($_ -split "`t")[5] -eq "XSRF-TOKEN" } |
        Select-Object -Last 1

    if (-not $line) {
        throw "XSRF-TOKEN cookie was not issued."
    }

    return ($line -split "`t")[6]
}

function Invoke-JsonApi {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [int[]]$ExpectedStatusCodes = @(200)
    )

    $xsrf = Get-XsrfToken
    $arguments = @(
        "-k",
        "-s",
        "-b", $cookiePath,
        "-c", $cookiePath,
        "-o", $responsePath,
        "-X", $Method,
        "-H", "RequestVerificationToken: $xsrf",
        "-H", "Content-Type: application/json"
    )

    if ($Body -ne $null) {
        $bodyPath = Join-Path $workDir ([Guid]::NewGuid().ToString("N") + ".json")
        $Body | ConvertTo-Json -Depth 20 -Compress | Set-Content -LiteralPath $bodyPath -Encoding UTF8
        $arguments += @("--data-binary", "@$bodyPath")
    }

    $arguments += "$ApiBaseUrl$Path"
    Invoke-Curl -Arguments $arguments -ExpectedStatusCodes $ExpectedStatusCodes -Name "$Method $Path" | Out-Null

    if ((Test-Path -LiteralPath $responsePath) -and (Get-Item -LiteralPath $responsePath).Length -gt 0) {
        return Get-Content -LiteralPath $responsePath -Raw -Encoding UTF8 | ConvertFrom-Json
    }

    return $null
}

function New-QuestionBody {
    param(
        [string]$Title,
        [string]$Stem,
        [int]$Type,
        [string[]]$Answers,
        [array]$Options,
        [string]$KnowledgePoint
    )

    return @{
        type = $Type
        difficulty = 1
        creationSource = 0
        title = $Title
        stem = $Stem
        securityLevel = 1
        tags = "$SeedPrefix,exam,demo"
        analysis = "$Title 标准解析"
        options = $Options
        answers = @($Answers | ForEach-Object -Begin { $i = 0 } -Process {
            $i++
            @{
                value = $_
                displayOrder = $i
            }
        })
        knowledgePointNames = @($KnowledgePoint)
        sources = @(
            @{
                manualSourceDescription = "$SeedPrefix synthetic exam demo source"
            }
        )
    }
}

function New-ApprovedQuestion {
    param([hashtable]$Body)

    $question = Invoke-JsonApi -Method "POST" -Path "/api/question-bank/questions" -Body $Body
    $question = Invoke-JsonApi -Method "POST" -Path "/api/question-bank/questions/$($question.id)/submit-review"
    return Invoke-JsonApi -Method "POST" -Path "/api/question-bank/questions/$($question.id)/approve" -Body @{
        comment = "MVP-2 demo seed approved"
    }
}

New-Item -ItemType Directory -Path $workDir -Force | Out-Null

try {
    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        throw "curl.exe is required."
    }

    if ([string]::IsNullOrWhiteSpace($RunTag)) {
        $RunTag = Get-Date -Format "yyyyMMddHHmmss"
    }

    Invoke-Curl -Arguments @(
        "-k",
        "-s",
        "-c", $cookiePath,
        "-o", $loginPagePath,
        "$ApiBaseUrl/Account/Login"
    ) -ExpectedStatusCodes @(200) -Name "GET /Account/Login" | Out-Null

    $loginHtml = Get-Content -LiteralPath $loginPagePath -Raw
    $loginToken = [regex]::Match($loginHtml, 'name="__RequestVerificationToken" type="hidden" value="([^"]+)"').Groups[1].Value
    if (-not $loginToken) {
        throw "Unable to parse login antiforgery token."
    }

    Invoke-Curl -Arguments @(
        "-k",
        "-s",
        "-L",
        "-b", $cookiePath,
        "-c", $cookiePath,
        "-o", (Join-Path $workDir "after-login.html"),
        "-X", "POST",
        "$ApiBaseUrl/Account/Login",
        "--data-urlencode", "LoginInput.UserNameOrEmailAddress=$AdminUserName",
        "--data-urlencode", "LoginInput.Password=$AdminPassword",
        "--data-urlencode", "LoginInput.RememberMe=false",
        "--data-urlencode", "Action=Login",
        "--data-urlencode", "__RequestVerificationToken=$loginToken"
    ) -ExpectedStatusCodes @(200, 302, 400) -Name "POST /Account/Login" | Out-Null

    Invoke-Curl -Arguments @(
        "-k",
        "-s",
        "-b", $cookiePath,
        "-c", $cookiePath,
        "-o", $responsePath,
        "$ApiBaseUrl/api/abp/application-configuration?IncludeLocalizationResources=False&api-version=1.0"
    ) -ExpectedStatusCodes @(200) -Name "GET /api/abp/application-configuration" | Out-Null

    $applicationConfiguration = Get-Content -LiteralPath $responsePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $applicationConfiguration.currentUser.isAuthenticated -or $applicationConfiguration.currentUser.userName -ne $AdminUserName) {
        throw "Admin login verification failed."
    }

    $point = "$SeedPrefix-$RunTag"
    $single = New-ApprovedQuestion -Body (New-QuestionBody `
        -Title "$SeedPrefix 单选题 $RunTag" `
        -Stem "KnowPulse MVP-2 考试闭环演示单选题：请选择 A。" `
        -Type 0 `
        -Answers @("A") `
        -KnowledgePoint $point `
        -Options @(
            @{ optionKey = "A"; content = "正确选项 A"; displayOrder = 1 },
            @{ optionKey = "B"; content = "干扰选项 B"; displayOrder = 2 },
            @{ optionKey = "C"; content = "干扰选项 C"; displayOrder = 3 }
        ))

    $trueFalse = New-ApprovedQuestion -Body (New-QuestionBody `
        -Title "$SeedPrefix 判断题 $RunTag" `
        -Stem "KnowPulse MVP-2 考试闭环要求提交后禁止修改。此说法是否正确？" `
        -Type 2 `
        -Answers @("true") `
        -KnowledgePoint $point `
        -Options @())

    $startTime = (Get-Date).ToUniversalTime().AddMinutes(-5).ToString("o")
    $endTime = (Get-Date).ToUniversalTime().AddHours(4).ToString("o")
    $plan = Invoke-JsonApi -Method "POST" -Path "/api/app/exam/plan" -Body @{
        name = "$SeedPrefix 考试闭环 $RunTag"
        description = "Synthetic MVP-2 demo exam generated by ops/seed-mvp2-demo-data.ps1."
        startTime = $startTime
        endTime = $endTime
        durationMinutes = $DurationMinutes
        passingScore = 10
        maxAttempts = 1
        resultVisibility = 1
        paper = @{
            title = "$SeedPrefix 试卷快照 $RunTag"
            buildMode = 0
            randomize = $false
            fixedQuestionIds = @($single.id, $trueFalse.id)
            rules = @(
                @{
                    count = 1
                    scorePerQuestion = 10
                    multipleChoiceScoringMode = 0
                }
            )
        }
        assignments = @(
            @{
                scopeType = 0
                targetId = $null
            }
        )
    }

    Write-Host "KnowPulse MVP-2 demo seed completed."
    [pscustomobject]@{
        RunTag = $RunTag
        KnowledgePoint = $point
        QuestionIds = @($single.id, $trueFalse.id)
        PlanId = $plan.id
        PlanName = $plan.name
        PaperId = $plan.paperId
        QuestionCount = $plan.paper.questionCount
        TotalScore = $plan.paper.totalScore
    } | ConvertTo-Json -Depth 5
}
finally {
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
