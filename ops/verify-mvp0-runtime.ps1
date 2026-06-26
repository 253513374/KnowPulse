[CmdletBinding()]
param(
    [string]$ApiBaseUrl = "https://localhost:44305",
    [string]$AdminUserName = "admin",
    [string]$AdminPassword = "1q2w3E*",
    [string]$MinioBucketName = "knowpulse-knowledge",
    [switch]$SkipMinioObjectCheck,
    [switch]$KeepAttachment
)

$ErrorActionPreference = "Stop"

$runtimeId = "rt-" + (Get-Date -Format "yyyyMMddHHmmss")
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) "knowpulse-mvp0-runtime-$runtimeId"
$cookiePath = Join-Path $workDir "cookies.txt"
$loginPagePath = Join-Path $workDir "login.html"
$responsePath = Join-Path $workDir "response.json"
$downloadPath = Join-Path $workDir "download.txt"
$attachmentPath = Join-Path $workDir "attachment.txt"

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
        return Get-Content -LiteralPath $responsePath -Raw | ConvertFrom-Json
    }

    return $null
}

function Get-MinioObjectFileCount {
    if ($SkipMinioObjectCheck) {
        return $null
    }

    $script = "if [ -d /data/$MinioBucketName ]; then ls -laR /data/$MinioBucketName | wc -l; else echo 0; fi"
    $output = & docker compose exec -T minio sh -c $script
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect MinIO bucket files."
    }

    return [int]($output | Select-Object -Last 1)
}

function Invoke-AuditScalar {
    param([string]$Sql)

    $output = & docker compose exec -T postgres psql -U root -d KnowPulse -At -v ON_ERROR_STOP=1 -c $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "Audit SQL failed."
    }

    return ($output | Select-Object -Last 1).Trim()
}

New-Item -ItemType Directory -Path $workDir -Force | Out-Null

try {
    if (-not (Get-Command curl.exe -ErrorAction SilentlyContinue)) {
        throw "curl.exe is required."
    }

    "MVP-0 runtime attachment content $runtimeId" | Set-Content -LiteralPath $attachmentPath -Encoding UTF8

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

    $applicationConfiguration = Get-Content -LiteralPath $responsePath -Raw | ConvertFrom-Json
    if (-not $applicationConfiguration.currentUser.isAuthenticated -or $applicationConfiguration.currentUser.userName -ne $AdminUserName) {
        throw "Admin login verification failed."
    }
    Add-Result -Name "Admin cookie login" -Passed $true -Detail $applicationConfiguration.currentUser.userName
    $adminUserId = $applicationConfiguration.currentUser.id
    if (-not $adminUserId) {
        $adminUserId = Invoke-AuditScalar "SELECT ""Id"" FROM ""AbpUsers"" WHERE ""UserName"" = '$AdminUserName' LIMIT 1;"
    }
    $adminRoleId = Invoke-AuditScalar "SELECT ""Id"" FROM ""AbpRoles"" WHERE ""Name"" = 'admin' LIMIT 1;"
    if (-not $adminUserId -or -not $adminRoleId) {
        throw "Unable to resolve admin user or role id."
    }

    $minioBefore = Get-MinioObjectFileCount

    $department = Invoke-JsonApi -Method "POST" -Path "/api/app/department" -Body @{
        code = "D-$runtimeId"
        name = "Runtime Department $runtimeId"
        securityLevel = 2
        isActive = $true
    }
    Add-Result -Name "Create department API" -Passed ([bool]$department.id) -Detail $department.id

    $position = Invoke-JsonApi -Method "POST" -Path "/api/app/position" -Body @{
        departmentId = $department.id
        code = "P-$runtimeId"
        name = "Runtime Position $runtimeId"
        securityLevel = 3
        isActive = $true
    }
    $updatedPosition = Invoke-JsonApi -Method "PUT" -Path "/api/app/position/$($position.id)" -Body @{
        departmentId = $department.id
        code = "P-$runtimeId"
        name = "Runtime Position Updated $runtimeId"
        securityLevel = 4
        isActive = $true
    }
    Add-Result -Name "Create and update position API" -Passed ([bool]$position.id -and $updatedPosition.securityLevel -eq 4) -Detail $position.id

    $jobLevel = Invoke-JsonApi -Method "POST" -Path "/api/app/job-level" -Body @{
        code = "J-$runtimeId"
        name = "Runtime JobLevel $runtimeId"
        sortOrder = 10
        securityLevel = 3
        isActive = $true
    }
    $updatedJobLevel = Invoke-JsonApi -Method "PUT" -Path "/api/app/job-level/$($jobLevel.id)" -Body @{
        code = "J-$runtimeId"
        name = "Runtime JobLevel Updated $runtimeId"
        sortOrder = 11
        securityLevel = 4
        isActive = $true
    }
    Add-Result -Name "Create and update job level API" -Passed ([bool]$jobLevel.id -and $updatedJobLevel.sortOrder -eq 11 -and $updatedJobLevel.securityLevel -eq 4) -Detail $jobLevel.id

    $assignment = Invoke-JsonApi -Method "POST" -Path "/api/app/user-organization-assignment" -Body @{
        userId = $adminUserId
        departmentId = $department.id
        positionId = $position.id
        jobLevelId = $jobLevel.id
        securityLevel = 4
        isPrimary = $false
        isActive = $true
    }
    $updatedAssignment = Invoke-JsonApi -Method "PUT" -Path "/api/app/user-organization-assignment/$($assignment.id)" -Body @{
        userId = $adminUserId
        departmentId = $department.id
        positionId = $position.id
        jobLevelId = $jobLevel.id
        securityLevel = 5
        isPrimary = $false
        isActive = $true
    }
    Add-Result -Name "Create and update user assignment API" -Passed ([bool]$assignment.id -and $updatedAssignment.securityLevel -eq 5) -Detail $assignment.id

    $category = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-category" -Body @{
        code = "C-$runtimeId"
        name = "Runtime Category $runtimeId"
        isActive = $true
    }
    Add-Result -Name "Create knowledge category API" -Passed ([bool]$category.id) -Detail $category.id

    $editableCategory = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-category" -Body @{
        code = "C-edit-$runtimeId"
        name = "Runtime Category Edit $runtimeId"
        isActive = $true
    }
    $updatedCategory = Invoke-JsonApi -Method "PUT" -Path "/api/app/knowledge-category/$($editableCategory.id)" -Body @{
        code = "C-edit-$runtimeId"
        name = "Runtime Category Edited $runtimeId"
        isActive = $false
    }
    Invoke-JsonApi -Method "DELETE" -Path "/api/app/knowledge-category/$($editableCategory.id)" -ExpectedStatusCodes @(200, 204) | Out-Null
    Add-Result -Name "Create update delete category API" -Passed ([bool]$editableCategory.id -and $updatedCategory.isActive -eq $false) -Detail $editableCategory.id

    $tag = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-tag" -Body @{
        name = "Runtime Tag $runtimeId"
        isActive = $true
    }
    $updatedTag = Invoke-JsonApi -Method "PUT" -Path "/api/app/knowledge-tag/$($tag.id)" -Body @{
        name = "Runtime Tag Edited $runtimeId"
        isActive = $false
    }
    Invoke-JsonApi -Method "DELETE" -Path "/api/app/knowledge-tag/$($tag.id)" -ExpectedStatusCodes @(200, 204) | Out-Null
    Add-Result -Name "Create update delete tag API" -Passed ([bool]$tag.id -and $updatedTag.isActive -eq $false) -Detail $tag.id

    $item = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item" -Body @{
        categoryId = $category.id
        title = "Runtime Knowledge $runtimeId"
        summary = "Runtime MVP-0 verification item"
    }
    Add-Result -Name "Create knowledge item API" -Passed ([bool]$item.id) -Detail $item.id

    $version = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$($item.id)/version" -Body @{
        title = "Runtime Version $runtimeId"
        contentText = "Runtime content should not appear in audit parameters $runtimeId"
    }
    Add-Result -Name "Create knowledge version API" -Passed ([bool]$version.id) -Detail $version.id

    $publishedVersion = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$($item.id)/publish-version/$($version.id)"
    Add-Result -Name "Publish knowledge version API" -Passed ($publishedVersion.status -eq 1 -and $publishedVersion.isCurrentPublished) -Detail "Status=$($publishedVersion.status)"

    $scope = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$($item.id)/permission-scope" -Body @{
        scopeType = 0
        minimumSecurityLevel = 0
    }
    Add-Result -Name "Create all-user permission scope API" -Passed ([bool]$scope.id) -Detail $scope.id

    $departmentScope = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$($item.id)/permission-scope" -Body @{
        scopeType = 1
        targetId = $department.id
        minimumSecurityLevel = 1
    }
    Add-Result -Name "Create department permission scope API" -Passed ([bool]$departmentScope.id -and $departmentScope.scopeType -eq 1 -and $departmentScope.minimumSecurityLevel -eq 1) -Detail $departmentScope.id

    $positionScope = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$($item.id)/permission-scope" -Body @{
        scopeType = 2
        targetId = $position.id
        minimumSecurityLevel = 2
    }
    Add-Result -Name "Create position permission scope API" -Passed ([bool]$positionScope.id -and $positionScope.scopeType -eq 2 -and $positionScope.minimumSecurityLevel -eq 2) -Detail $positionScope.id

    $roleScope = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$($item.id)/permission-scope" -Body @{
        scopeType = 3
        targetId = $adminRoleId
        minimumSecurityLevel = 3
    }
    Add-Result -Name "Create role permission scope API" -Passed ([bool]$roleScope.id -and $roleScope.scopeType -eq 3 -and $roleScope.minimumSecurityLevel -eq 3) -Detail $roleScope.id

    $userScope = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$($item.id)/permission-scope" -Body @{
        scopeType = 4
        targetId = $adminUserId
        minimumSecurityLevel = 4
    }
    Add-Result -Name "Create user permission scope API" -Passed ([bool]$userScope.id -and $userScope.scopeType -eq 4 -and $userScope.minimumSecurityLevel -eq 4) -Detail $userScope.id

    $xsrf = Get-XsrfToken
    Invoke-Curl -Arguments @(
        "-k",
        "-s",
        "-b", $cookiePath,
        "-c", $cookiePath,
        "-o", $responsePath,
        "-X", "POST",
        "-H", "RequestVerificationToken: $xsrf",
        "-F", "file=@$attachmentPath;type=text/plain",
        "$ApiBaseUrl/api/app/knowledge-item/$($item.id)/upload-attachment/$($version.id)"
    ) -ExpectedStatusCodes @(200) -Name "POST upload attachment" | Out-Null

    $attachment = Get-Content -LiteralPath $responsePath -Raw | ConvertFrom-Json
    Add-Result -Name "Upload attachment API" -Passed ([bool]$attachment.id) -Detail $attachment.fileName

    $minioAfterUpload = Get-MinioObjectFileCount
    if (-not $SkipMinioObjectCheck) {
        Add-Result -Name "MinIO object written" -Passed ($minioAfterUpload -gt $minioBefore) -Detail "Before=$minioBefore AfterUpload=$minioAfterUpload"
    }

    Invoke-Curl -Arguments @(
        "-k",
        "-s",
        "-b", $cookiePath,
        "-c", $cookiePath,
        "-o", $downloadPath,
        "-X", "POST",
        "-H", "RequestVerificationToken: $xsrf",
        "$ApiBaseUrl/api/app/knowledge-item/$($item.id)/download-attachment?versionId=$($version.id)&attachmentId=$($attachment.id)"
    ) -ExpectedStatusCodes @(200) -Name "POST download attachment" | Out-Null

    $downloaded = Get-Content -LiteralPath $downloadPath -Raw
    Add-Result -Name "Download attachment API" -Passed ($downloaded -like "*$runtimeId*") -Detail "Downloaded content matched"

    if (-not $KeepAttachment) {
        Invoke-JsonApi -Method "DELETE" -Path "/api/app/knowledge-item/$($item.id)/attachment?versionId=$($version.id)&attachmentId=$($attachment.id)" -ExpectedStatusCodes @(200, 204) | Out-Null
        $minioAfterDelete = Get-MinioObjectFileCount
        if (-not $SkipMinioObjectCheck) {
            Add-Result -Name "Delete attachment removed MinIO object" -Passed ($minioAfterDelete -lt $minioAfterUpload) -Detail "AfterUpload=$minioAfterUpload AfterDelete=$minioAfterDelete"
        }
    }

    $withdrawnVersion = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$($item.id)/withdraw-version/$($version.id)"
    Add-Result -Name "Withdraw knowledge version API" -Passed ($withdrawnVersion.status -eq 2 -and -not $withdrawnVersion.isCurrentPublished) -Detail "Status=$($withdrawnVersion.status)"

    $archivedVersion = Invoke-JsonApi -Method "POST" -Path "/api/app/knowledge-item/$($item.id)/archive-version/$($version.id)"
    Add-Result -Name "Archive knowledge version API" -Passed ($archivedVersion.status -eq 3) -Detail "Status=$($archivedVersion.status)"

    $auditMethodCountSql = @"
SELECT COUNT(DISTINCT a."MethodName")
FROM "AbpAuditLogs" l
JOIN "AbpAuditLogActions" a ON a."AuditLogId" = l."Id"
WHERE l."ExecutionTime" >= NOW() - INTERVAL '15 minutes'
  AND a."ServiceName" ILIKE '%KnowPulse%'
  AND a."MethodName" IN (
    'CreateAsync',
    'CreateVersionAsync',
    'PublishVersionAsync',
    'CreatePermissionScopeAsync',
    'UploadAttachmentAsync',
    'DownloadAttachmentAsync',
    'DeleteAttachmentAsync',
    'WithdrawVersionAsync',
    'ArchiveVersionAsync'
  );
"@
    $auditMethodCount = [int](Invoke-AuditScalar $auditMethodCountSql)
    Add-Result -Name "Runtime audit actions written" -Passed ($auditMethodCount -ge 8) -Detail "DistinctMethodCount=$auditMethodCount"

    $sensitiveHitsSql = @"
SELECT SUM("Hits")
FROM (
    SELECT COUNT(*) AS "Hits"
    FROM "AbpAuditLogActions"
    WHERE COALESCE("Parameters", '') ILIKE '%ContentText%'
       OR COALESCE("Parameters", '') ILIKE '%IRemoteStreamContent%'
       OR COALESCE("Parameters", '') ILIKE '%RemoteStreamContent%'
    UNION ALL
    SELECT COUNT(*) AS "Hits"
    FROM "AbpEntityPropertyChanges"
    WHERE "PropertyName" ILIKE '%ContentText%'
       OR COALESCE("OriginalValue", '') ILIKE '%ContentText%'
       OR COALESCE("NewValue", '') ILIKE '%ContentText%'
) s;
"@
    $sensitiveHits = [int](Invoke-AuditScalar $sensitiveHitsSql)
    Add-Result -Name "Audit sensitive field scan" -Passed ($sensitiveHits -eq 0) -Detail "Hits=$sensitiveHits"
}
finally {
    Write-Host ""
    Write-Host "MVP-0 runtime verification summary"
    $results | Format-Table -AutoSize
}

if ($results | Where-Object { -not $_.Passed }) {
    exit 1
}
