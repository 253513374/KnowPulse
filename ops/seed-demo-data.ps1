[CmdletBinding()]
param(
    [string]$ApiBaseUrl = "https://localhost:44305",
    [string]$AdminUserName = "admin",
    [string]$AdminPassword = "1q2w3E*",
    [string]$SeedPrefix = "DEMO",
    [string]$AttachmentPath = "",
    [switch]$SkipAttachmentDownloadCheck
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$seedDir = Join-Path $repoRoot "artifacts\demo-seed"
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("knowpulse-demo-seed-" + [Guid]::NewGuid().ToString("N"))
$cookiePath = Join-Path $workDir "cookies.txt"
$loginPagePath = Join-Path $workDir "login.html"
$responsePath = Join-Path $workDir "response.json"
$downloadPath = Join-Path $workDir "download.txt"

$created = New-Object System.Collections.Generic.List[string]
$reused = New-Object System.Collections.Generic.List[string]

function Add-Created {
    param([string]$Value)
    $created.Add($Value) | Out-Null
}

function Add-Reused {
    param([string]$Value)
    $reused.Add($Value) | Out-Null
}

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

    $bodyPath = $null
    if ($Body -ne $null) {
        $bodyPath = Join-Path $workDir ([Guid]::NewGuid().ToString("N") + ".json")
        $Body | ConvertTo-Json -Depth 10 -Compress | Set-Content -LiteralPath $bodyPath -Encoding UTF8
        $arguments += @("--data-binary", "@$bodyPath")
    }

    $arguments += "$ApiBaseUrl$Path"
    Invoke-Curl -Arguments $arguments -ExpectedStatusCodes $ExpectedStatusCodes -Name "$Method $Path" | Out-Null

    if ((Test-Path -LiteralPath $responsePath) -and (Get-Item -LiteralPath $responsePath).Length -gt 0) {
        return Get-Content -LiteralPath $responsePath -Raw -Encoding UTF8 | ConvertFrom-Json
    }

    return $null
}

function Get-PagedItems {
    param([string]$Path)

    $separator = if ($Path.Contains("?")) { "&" } else { "?" }
    $result = Invoke-JsonApi -Method "GET" -Path "$Path${separator}SkipCount=0&MaxResultCount=1000"
    if ($null -eq $result.items) {
        return @()
    }

    return @($result.items)
}

function Find-ByProperty {
    param(
        [object[]]$Items,
        [string]$Property,
        [object]$Value
    )

    return $Items | Where-Object { $_.$Property -eq $Value } | Select-Object -First 1
}

function Ensure-Department {
    param(
        [string]$Code,
        [string]$Name,
        [int]$SecurityLevel
    )

    $existing = Find-ByProperty -Items (Get-PagedItems "/api/app/department") -Property "code" -Value $Code
    $body = @{
        code = $Code
        name = $Name
        securityLevel = $SecurityLevel
        isActive = $true
    }

    if ($existing) {
        Add-Reused "Department $Code"
        return Invoke-JsonApi -Method "PUT" -Path "/api/app/department/$($existing.id)" -Body $body
    }

    Add-Created "Department $Code"
    return Invoke-JsonApi -Method "POST" -Path "/api/app/department" -Body $body
}

function Ensure-Position {
    param(
        [string]$Code,
        [string]$Name,
        [string]$DepartmentId,
        [int]$SecurityLevel
    )

    $existing = Find-ByProperty -Items (Get-PagedItems "/api/app/position") -Property "code" -Value $Code
    $body = @{
        departmentId = $DepartmentId
        code = $Code
        name = $Name
        securityLevel = $SecurityLevel
        isActive = $true
    }

    if ($existing) {
        Add-Reused "Position $Code"
        return Invoke-JsonApi -Method "PUT" -Path "/api/app/position/$($existing.id)" -Body $body
    }

    Add-Created "Position $Code"
    return Invoke-JsonApi -Method "POST" -Path "/api/app/position" -Body $body
}

function Ensure-JobLevel {
    param(
        [string]$Code,
        [string]$Name,
        [int]$SortOrder,
        [int]$SecurityLevel
    )

    $existing = Find-ByProperty -Items (Get-PagedItems "/api/app/job-level") -Property "code" -Value $Code
    $body = @{
        code = $Code
        name = $Name
        sortOrder = $SortOrder
        securityLevel = $SecurityLevel
        isActive = $true
    }

    if ($existing) {
        Add-Reused "JobLevel $Code"
        return Invoke-JsonApi -Method "PUT" -Path "/api/app/job-level/$($existing.id)" -Body $body
    }

    Add-Created "JobLevel $Code"
    return Invoke-JsonApi -Method "POST" -Path "/api/app/job-level" -Body $body
}

function Ensure-Category {
    param(
        [string]$Code,
        [string]$Name
    )

    $existing = Find-ByProperty -Items (Get-PagedItems "/api/app/knowledge-category") -Property "code" -Value $Code
    $body = @{
        code = $Code
        name = $Name
        isActive = $true
    }

    if ($existing) {
        Add-Reused "Category $Code"
        return Invoke-JsonApi -Method "PUT" -Path "/api/app/knowledge-category/$($existing.id)" -Body $body
    }

    Add-Created "Category $Code"
    return Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-category" -Body $body
}

function Ensure-Tag {
    param([string]$Name)

    $existing = Find-ByProperty -Items (Get-PagedItems "/api/app/knowledge-tag") -Property "name" -Value $Name
    $body = @{
        name = $Name
        isActive = $true
    }

    if ($existing) {
        Add-Reused "Tag $Name"
        return Invoke-JsonApi -Method "PUT" -Path "/api/app/knowledge-tag/$($existing.id)" -Body $body
    }

    Add-Created "Tag $Name"
    return Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-tag" -Body $body
}

function Ensure-KnowledgeItem {
    param(
        [string]$Title,
        [string]$Summary,
        [string]$CategoryId
    )

    $existing = Find-ByProperty -Items (Get-PagedItems "/api/app/knowledge-item") -Property "title" -Value $Title
    if ($existing) {
        Add-Reused "KnowledgeItem $Title"
        return Invoke-JsonApi -Method "PUT" -Path "/api/app/knowledge-item/$($existing.id)" -Body @{
            categoryId = $CategoryId
            title = $Title
            summary = $Summary
            isActive = $true
        }
    }

    Add-Created "KnowledgeItem $Title"
    return Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item" -Body @{
        categoryId = $CategoryId
        title = $Title
        summary = $Summary
    }
}

function Ensure-Version {
    param(
        [string]$ItemId,
        [string]$Title,
        [string]$ContentText
    )

    $versions = Invoke-JsonApi -Method "GET" -Path "/api/app/knowledge-item/$ItemId/versions"
    $existing = @($versions.items) | Where-Object { $_.title -eq $Title } | Select-Object -First 1
    if ($existing) {
        Add-Reused "Version $Title"
        if (-not $existing.isCurrentPublished) {
            return Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$ItemId/publish-version/$($existing.id)"
        }

        return $existing
    }

    Add-Created "Version $Title"
    $version = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$ItemId/version" -Body @{
        title = $Title
        contentText = $ContentText
    }

    return Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$ItemId/publish-version/$($version.id)"
}

function Ensure-PermissionScope {
    param(
        [string]$ItemId,
        [int]$ScopeType,
        [string]$TargetId = "",
        [int]$MinimumSecurityLevel = 0
    )

    $scopes = Invoke-JsonApi -Method "GET" -Path "/api/app/knowledge-item/$ItemId/permission-scopes"
    $existing = @($scopes.items) | Where-Object {
        $_.scopeType -eq $ScopeType -and
        "$($_.targetId)" -eq $TargetId -and
        ($_.minimumSecurityLevel -eq $MinimumSecurityLevel -or ($null -eq $_.minimumSecurityLevel -and $MinimumSecurityLevel -eq 0))
    } | Select-Object -First 1

    if ($existing) {
        Add-Reused "PermissionScope item=$ItemId type=$ScopeType"
        return $existing
    }

    $body = @{
        scopeType = $ScopeType
        minimumSecurityLevel = $MinimumSecurityLevel
    }

    if (-not [string]::IsNullOrWhiteSpace($TargetId)) {
        $body.targetId = $TargetId
    }

    Add-Created "PermissionScope item=$ItemId type=$ScopeType"
    return Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$ItemId/permission-scope" -Body $body
}

function Ensure-UserAssignment {
    param(
        [string]$UserId,
        [string]$DepartmentId,
        [string]$PositionId,
        [string]$JobLevelId,
        [int]$SecurityLevel
    )

    $existing = @((Get-PagedItems "/api/app/user-organization-assignment")) | Where-Object {
        "$($_.userId)" -eq $UserId -and "$($_.departmentId)" -eq $DepartmentId
    } | Select-Object -First 1

    $body = @{
        userId = $UserId
        departmentId = $DepartmentId
        positionId = $PositionId
        jobLevelId = $JobLevelId
        securityLevel = $SecurityLevel
        isPrimary = $true
        isActive = $true
    }

    if ($existing) {
        Add-Reused "UserAssignment admin->$DepartmentId"
        return Invoke-JsonApi -Method "PUT" -Path "/api/app/user-organization-assignment/$($existing.id)" -Body $body
    }

    Add-Created "UserAssignment admin->$DepartmentId"
    return Invoke-JsonApi -Method "POST" -Path "/api/app/user-organization-assignment" -Body $body
}

New-Item -ItemType Directory -Path $workDir -Force | Out-Null
New-Item -ItemType Directory -Path $seedDir -Force | Out-Null

try {
    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        throw "curl.exe is required."
    }

    if ([string]::IsNullOrWhiteSpace($AttachmentPath)) {
        $AttachmentPath = Join-Path $seedDir "knowpulse-demo-attachment.txt"
    }

    if (-not (Test-Path -LiteralPath $AttachmentPath)) {
        @(
            "KnowPulse demo attachment",
            "Generated by ops/seed-demo-data.ps1.",
            "This file contains synthetic demo content only."
        ) | Set-Content -LiteralPath $AttachmentPath -Encoding UTF8
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

    $adminUserId = $applicationConfiguration.currentUser.id
    $roles = Get-PagedItems "/api/identity/roles"
    $adminRole = $roles | Where-Object { $_.name -eq "admin" } | Select-Object -First 1
    if (-not $adminRole) {
        throw "Unable to resolve admin role from /api/identity/roles."
    }

    $rd = Ensure-Department -Code "$SeedPrefix-RD" -Name "Research Center" -SecurityLevel 3
    $sales = Ensure-Department -Code "$SeedPrefix-SALES" -Name "Sales Center" -SecurityLevel 2
    $support = Ensure-Department -Code "$SeedPrefix-SUPPORT" -Name "Customer Success Center" -SecurityLevel 1

    $architect = Ensure-Position -Code "$SeedPrefix-ARCH" -Name "Architect" -DepartmentId $rd.id -SecurityLevel 4
    $consultant = Ensure-Position -Code "$SeedPrefix-CONSULT" -Name "Solution Consultant" -DepartmentId $sales.id -SecurityLevel 2
    $supportLead = Ensure-Position -Code "$SeedPrefix-CS-LEAD" -Name "Customer Success Lead" -DepartmentId $support.id -SecurityLevel 2

    $l1 = Ensure-JobLevel -Code "$SeedPrefix-L1" -Name "Junior" -SortOrder 10 -SecurityLevel 1
    $l2 = Ensure-JobLevel -Code "$SeedPrefix-L2" -Name "Middle" -SortOrder 20 -SecurityLevel 2
    $l3 = Ensure-JobLevel -Code "$SeedPrefix-L3" -Name "Senior" -SortOrder 30 -SecurityLevel 4

    Ensure-UserAssignment -UserId $adminUserId -DepartmentId $rd.id -PositionId $architect.id -JobLevelId $l3.id -SecurityLevel 4 | Out-Null

    $productCategory = Ensure-Category -Code "$SeedPrefix-KB-PRODUCT" -Name "Product Knowledge"
    $processCategory = Ensure-Category -Code "$SeedPrefix-KB-PROCESS" -Name "Process Guide"
    $caseCategory = Ensure-Category -Code "$SeedPrefix-KB-CASE" -Name "Customer Case"

    Ensure-Tag -Name "$SeedPrefix-Intro" | Out-Null
    Ensure-Tag -Name "$SeedPrefix-Permission" | Out-Null
    Ensure-Tag -Name "$SeedPrefix-RAG" | Out-Null

    $guideItem = Ensure-KnowledgeItem `
        -Title "$SeedPrefix KnowPulse MVP-0 Guide" `
        -Summary "Demo knowledge lifecycle from item creation to version publish, permission scope, and attachment flow." `
        -CategoryId $productCategory.id
    $guideVersion = Ensure-Version `
        -ItemId $guideItem.id `
        -Title "$SeedPrefix Initial Published Version" `
        -ContentText "This is synthetic KnowPulse MVP-0 demo content for local testing only."

    $permissionItem = Ensure-KnowledgeItem `
        -Title "$SeedPrefix Knowledge Permission Scope Example" `
        -Summary "Demo All, Department, Position, Role, User, and security-level scopes." `
        -CategoryId $processCategory.id
    $permissionVersion = Ensure-Version `
        -ItemId $permissionItem.id `
        -Title "$SeedPrefix Permission Scope Version" `
        -ContentText "This item verifies backend permission enforcement without relying on hidden frontend buttons."

    $ragItem = Ensure-KnowledgeItem `
        -Title "$SeedPrefix RAG and AI Boundary Note" `
        -Summary "Demo note for MVP-0 RAG/AI boundary, banned provider list, and health checks." `
        -CategoryId $caseCategory.id
    $ragVersion = Ensure-Version `
        -ItemId $ragItem.id `
        -Title "$SeedPrefix RAG Boundary Version" `
        -ContentText "MVP-0 does not implement real RAG, vector writes, AI question generation, or agent orchestration."

    Ensure-PermissionScope -ItemId $guideItem.id -ScopeType 0 -MinimumSecurityLevel 0 | Out-Null
    Ensure-PermissionScope -ItemId $permissionItem.id -ScopeType 1 -TargetId $rd.id -MinimumSecurityLevel 2 | Out-Null
    Ensure-PermissionScope -ItemId $permissionItem.id -ScopeType 2 -TargetId $architect.id -MinimumSecurityLevel 3 | Out-Null
    Ensure-PermissionScope -ItemId $permissionItem.id -ScopeType 3 -TargetId $adminRole.id -MinimumSecurityLevel 0 | Out-Null
    Ensure-PermissionScope -ItemId $ragItem.id -ScopeType 4 -TargetId $adminUserId -MinimumSecurityLevel 4 | Out-Null

    $attachments = Invoke-JsonApi -Method "GET" -Path "/api/app/knowledge-item/$($guideItem.id)/attachments/$($guideVersion.id)"
    $attachmentFileName = Split-Path -Leaf $AttachmentPath
    $existingAttachment = @($attachments.items) | Where-Object { $_.fileName -eq $attachmentFileName } | Select-Object -First 1

    if ($existingAttachment) {
        Add-Reused "Attachment $attachmentFileName"
        $attachment = $existingAttachment
    }
    else {
        $xsrf = Get-XsrfToken
        Invoke-Curl -Arguments @(
            "-k",
            "-s",
            "-b", $cookiePath,
            "-c", $cookiePath,
            "-o", $responsePath,
            "-X", "POST",
            "-H", "RequestVerificationToken: $xsrf",
            "-F", "file=@$AttachmentPath;type=text/plain",
            "$ApiBaseUrl/api/app/knowledge-item/$($guideItem.id)/upload-attachment/$($guideVersion.id)"
        ) -ExpectedStatusCodes @(200) -Name "POST upload attachment" | Out-Null

        $attachment = Get-Content -LiteralPath $responsePath -Raw -Encoding UTF8 | ConvertFrom-Json
        Add-Created "Attachment $attachmentFileName"
    }

    if (-not $SkipAttachmentDownloadCheck) {
        $xsrf = Get-XsrfToken
        Invoke-Curl -Arguments @(
            "-k",
            "-s",
            "-b", $cookiePath,
            "-c", $cookiePath,
            "-o", $downloadPath,
            "-X", "POST",
            "-H", "RequestVerificationToken: $xsrf",
            "$ApiBaseUrl/api/app/knowledge-item/$($guideItem.id)/download-attachment?versionId=$($guideVersion.id)&attachmentId=$($attachment.id)"
        ) -ExpectedStatusCodes @(200) -Name "POST download attachment" | Out-Null

        $downloaded = Get-Content -LiteralPath $downloadPath -Raw
        if ($downloaded -notlike "*KnowPulse demo attachment*") {
            throw "Attachment download check failed."
        }
    }

    Write-Host ""
    Write-Host "KnowPulse demo seed completed."
    Write-Host "Created:"
    if ($created.Count -eq 0) {
        Write-Host "  (none)"
    }
    else {
        $created | Sort-Object | ForEach-Object { Write-Host "  $_" }
    }

    Write-Host "Reused or updated:"
    if ($reused.Count -eq 0) {
        Write-Host "  (none)"
    }
    else {
        $reused | Sort-Object | ForEach-Object { Write-Host "  $_" }
    }

    Write-Host ""
    Write-Host "Seed summary:"
    [pscustomobject]@{
        Departments = @($rd.code, $sales.code, $support.code) -join ", "
        Positions = @($architect.code, $consultant.code, $supportLead.code) -join ", "
        JobLevels = @($l1.code, $l2.code, $l3.code) -join ", "
        Categories = @($productCategory.code, $processCategory.code, $caseCategory.code) -join ", "
        KnowledgeItems = @($guideItem.title, $permissionItem.title, $ragItem.title) -join " | "
        Attachment = $attachment.fileName
    } | Format-List
}
finally {
    Remove-Item -LiteralPath $workDir -Recurse -Force -ErrorAction SilentlyContinue
}
