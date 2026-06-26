[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$checks = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )

    $checks.Add([pscustomobject]@{
        Name = $Name
        Passed = $Passed
        Detail = $Detail
    })
}

function Test-FileContains {
    param(
        [string]$Path,
        [string]$Pattern
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $match = Select-String -LiteralPath $Path -Pattern $Pattern -Quiet
    return [bool]$match
}

function Assert-FileContains {
    param(
        [string]$Name,
        [string]$Path,
        [string]$Pattern
    )

    Add-Check -Name $Name -Passed (Test-FileContains -Path $Path -Pattern $Pattern) -Detail $Path
}

function Assert-RequiredFiles {
    param(
        [string]$Name,
        [string[]]$Paths
    )

    $missing = @($Paths | Where-Object { -not (Test-Path -LiteralPath $_) })
    Add-Check -Name $Name -Passed ($missing.Count -eq 0) -Detail ($(if ($missing.Count -eq 0) { "All files exist" } else { "Missing: $($missing -join ', ')" }))
}

Assert-RequiredFiles -Name "MVP-0 architecture documents exist" -Paths @(
    "docs/system-architecture.md",
    "docs/technical-architecture.md",
    "docs/mvp-0-task-breakdown.md",
    "docs/mvp-0-qa-test-plan.md",
    "docs/mvp-0-acceptance-matrix.md",
    "docs/rag-ai-boundary.md",
    "docs/development-environment.md",
    "ops/verify-mvp0.ps1",
    "ops/verify-mvp0-runtime.ps1",
    "ops/seed-demo-data.ps1",
    "ops/check-page-attachment-prereqs.ps1"
)

Assert-RequiredFiles -Name "MVP-0 Blazor pages exist" -Paths @(
    "src/KnowPulse.Blazor.Client/Pages/Organization/Departments.razor",
    "src/KnowPulse.Blazor.Client/Pages/Organization/Positions.razor",
    "src/KnowPulse.Blazor.Client/Pages/Organization/JobLevels.razor",
    "src/KnowPulse.Blazor.Client/Pages/Organization/UserAssignments.razor",
    "src/KnowPulse.Blazor.Client/Pages/Knowledge/Categories.razor",
    "src/KnowPulse.Blazor.Client/Pages/Knowledge/Tags.razor",
    "src/KnowPulse.Blazor.Client/Pages/Knowledge/Items.razor",
    "src/KnowPulse.Blazor.Client/Pages/Knowledge/PermissionScopes.razor"
)

$pagePermissions = @{
    "src/KnowPulse.Blazor.Client/Pages/Organization/Departments.razor" = "KnowPulsePermissions.Organization.Departments"
    "src/KnowPulse.Blazor.Client/Pages/Organization/Positions.razor" = "KnowPulsePermissions.Organization.Positions"
    "src/KnowPulse.Blazor.Client/Pages/Organization/JobLevels.razor" = "KnowPulsePermissions.Organization.JobLevels"
    "src/KnowPulse.Blazor.Client/Pages/Organization/UserAssignments.razor" = "KnowPulsePermissions.Organization.UserAssignments"
    "src/KnowPulse.Blazor.Client/Pages/Knowledge/Categories.razor" = "KnowPulsePermissions.KnowledgeBase.Categories"
    "src/KnowPulse.Blazor.Client/Pages/Knowledge/Tags.razor" = "KnowPulsePermissions.KnowledgeBase.Tags"
    "src/KnowPulse.Blazor.Client/Pages/Knowledge/Items.razor" = "KnowPulsePermissions.KnowledgeBase.Items"
    "src/KnowPulse.Blazor.Client/Pages/Knowledge/PermissionScopes.razor" = "KnowPulsePermissions.KnowledgeBase.PermissionScopes"
}

foreach ($entry in $pagePermissions.GetEnumerator()) {
    Assert-FileContains -Name "Blazor page authorize: $($entry.Value)" -Path $entry.Key -Pattern "\[Authorize\($([regex]::Escape($entry.Value))\)\]"
}

$menuFile = "src/KnowPulse.Blazor.Client/Menus/KnowPulseMenuContributor.cs"
foreach ($permission in $pagePermissions.Values) {
    Assert-FileContains -Name "Menu requires permission: $permission" -Path $menuFile -Pattern "RequirePermissions\($([regex]::Escape($permission))\)"
}

$servicePermissionChecks = @{
    "src/KnowPulse.Application/Organization/DepartmentAppService.cs" = "KnowPulsePermissions.Organization.Departments"
    "src/KnowPulse.Application/Organization/PositionAppService.cs" = "KnowPulsePermissions.Organization.Positions"
    "src/KnowPulse.Application/Organization/JobLevelAppService.cs" = "KnowPulsePermissions.Organization.JobLevels"
    "src/KnowPulse.Application/Organization/UserOrganizationAssignmentAppService.cs" = "KnowPulsePermissions.Organization.UserAssignments"
    "src/KnowPulse.Application/KnowledgeBase/KnowledgeCategoryAppService.cs" = "KnowPulsePermissions.KnowledgeBase.Categories"
    "src/KnowPulse.Application/KnowledgeBase/KnowledgeTagAppService.cs" = "KnowPulsePermissions.KnowledgeBase.Tags"
    "src/KnowPulse.Application/KnowledgeBase/KnowledgeItemAppService.cs" = "KnowPulsePermissions.KnowledgeBase.Items"
}

foreach ($entry in $servicePermissionChecks.GetEnumerator()) {
    Assert-FileContains -Name "AppService checks permission: $($entry.Value)" -Path $entry.Key -Pattern "CheckMvpPermissionAsync\($([regex]::Escape($entry.Value))\)"
}

foreach ($permission in @(
    "KnowPulsePermissions.KnowledgeBase.Versions",
    "KnowPulsePermissions.KnowledgeBase.Attachments",
    "KnowPulsePermissions.KnowledgeBase.PermissionScopes"
)) {
    Assert-FileContains -Name "KnowledgeItemAppService checks permission: $permission" -Path "src/KnowPulse.Application/KnowledgeBase/KnowledgeItemAppService.cs" -Pattern "CheckMvpPermissionAsync\($([regex]::Escape($permission))\)"
}

Assert-FileContains -Name "MVP permission guard rejects unauthenticated users" -Path "src/KnowPulse.Application/KnowPulseAppService.cs" -Pattern "CurrentUser\.IsAuthenticated"
Assert-FileContains -Name "MVP permission guard uses IPermissionChecker" -Path "src/KnowPulse.Application/KnowPulseAppService.cs" -Pattern "IPermissionChecker"

Assert-FileContains -Name "Blazor OIDC client id configured" -Path "src/KnowPulse.Blazor.Client/wwwroot/appsettings.json" -Pattern '"ClientId"\s*:\s*"KnowPulse_Blazor"'
Assert-FileContains -Name "Blazor OIDC authority configured" -Path "src/KnowPulse.Blazor.Client/wwwroot/appsettings.json" -Pattern '"Authority"\s*:\s*"https://localhost:44305"'
Assert-FileContains -Name "DbMigrator seeds Blazor client" -Path "src/KnowPulse.DbMigrator/appsettings.json" -Pattern '"KnowPulse_Blazor"'
Assert-FileContains -Name "DbMigrator seeds Swagger client" -Path "src/KnowPulse.DbMigrator/appsettings.json" -Pattern '"KnowPulse_Swagger"'

Assert-FileContains -Name "API Host enables auditing middleware" -Path "src/KnowPulse.HttpApi.Host/KnowPulseHttpApiHostModule.cs" -Pattern "UseAuditing\(\)"
Assert-FileContains -Name "API Host audits GET requests" -Path "src/KnowPulse.HttpApi.Host/KnowPulseHttpApiHostModule.cs" -Pattern "IsEnabledForGetRequests\s*=\s*true"
Assert-FileContains -Name "Audit ignores knowledge content DTO" -Path "src/KnowPulse.HttpApi.Host/KnowPulseHttpApiHostModule.cs" -Pattern "IgnoredTypes\.Add\(typeof\(CreateKnowledgeVersionDto\)\)"
Assert-FileContains -Name "Audit ignores remote stream content" -Path "src/KnowPulse.HttpApi.Host/KnowPulseHttpApiHostModule.cs" -Pattern "IgnoredTypes\.Add\(typeof\(IRemoteStreamContent\)\)"
Assert-FileContains -Name "Background job EF Core module configured" -Path "src/KnowPulse.EntityFrameworkCore/EntityFrameworkCore/KnowPulseEntityFrameworkCoreModule.cs" -Pattern "AbpBackgroundJobsEntityFrameworkCoreModule"
Assert-FileContains -Name "Background job tables configured in DbContext" -Path "src/KnowPulse.EntityFrameworkCore/EntityFrameworkCore/KnowPulseDbContext.cs" -Pattern "ConfigureBackgroundJobs\(\)"
Assert-FileContains -Name "Background job migration includes AbpBackgroundJobs table" -Path "src/KnowPulse.EntityFrameworkCore/Migrations/20260624153023_Initial.cs" -Pattern '"AbpBackgroundJobs"'
Assert-FileContains -Name "Development docs mention background job storage reservation" -Path "docs/development-environment.md" -Pattern "Hangfire"
Assert-FileContains -Name "Audit check script exists" -Path "ops/audit-check.ps1" -Pattern "Sensitive audit field scan"
Assert-FileContains -Name "MVP-0 verify script checks local endpoints" -Path "ops/verify-mvp0.ps1" -Pattern "local endpoint check"
Assert-FileContains -Name "MVP-0 verify script can run authenticated runtime flow" -Path "ops/verify-mvp0.ps1" -Pattern "WithRuntime"
Assert-FileContains -Name "MVP-0 verify script can run demo seed data" -Path "ops/verify-mvp0.ps1" -Pattern "WithDemoSeed"
Assert-FileContains -Name "MVP-0 verify script can run page attachment preflight" -Path "ops/verify-mvp0.ps1" -Pattern "WithPageAttachmentPreflight"
Assert-FileContains -Name "MVP-0 runtime verifier uses cookie login" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "Account/Login"
Assert-FileContains -Name "MVP-0 runtime verifier uploads attachment" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "upload-attachment"
Assert-FileContains -Name "MVP-0 demo seed uses cookie login" -Path "ops/seed-demo-data.ps1" -Pattern "Account/Login"
Assert-FileContains -Name "MVP-0 demo seed uploads generated attachment" -Path "ops/seed-demo-data.ps1" -Pattern "knowpulse-demo-attachment\.txt"
Assert-FileContains -Name "Page attachment preflight checks native host" -Path "ops/check-page-attachment-prereqs.ps1" -Pattern "native messaging host"
Assert-FileContains -Name "Page attachment preflight uses Chrome plugin native-host checker" -Path "ops/check-page-attachment-prereqs.ps1" -Pattern "check-native-host-manifest\.js"
Assert-FileContains -Name "Page attachment preflight uses Chrome extension checker" -Path "ops/check-page-attachment-prereqs.ps1" -Pattern "check-extension-installed\.js"
Assert-FileContains -Name "Page attachment preflight separates native host registry check" -Path "ops/check-page-attachment-prereqs.ps1" -Pattern "Codex Chrome native host registry"
Assert-FileContains -Name "Page attachment preflight separates native host official check" -Path "ops/check-page-attachment-prereqs.ps1" -Pattern "Codex Chrome native host official check"
Assert-FileContains -Name "Page attachment preflight documents attachment file input" -Path "ops/check-page-attachment-prereqs.ps1" -Pattern "#attachment-file"
Assert-FileContains -Name "Page attachment preflight can require strict endpoints" -Path "ops/check-page-attachment-prereqs.ps1" -Pattern "RequireEndpoints"
Assert-FileContains -Name "Development docs mention strict page attachment preflight" -Path "docs/development-environment.md" -Pattern "RequireBrowserAutomation"
Assert-FileContains -Name "Acceptance matrix mentions strict page attachment preflight" -Path "docs/mvp-0-acceptance-matrix.md" -Pattern "RequireEndpoints -RequireBrowserAutomation"
Assert-FileContains -Name "Acceptance matrix mentions native host official check" -Path "docs/mvp-0-acceptance-matrix.md" -Pattern "native host official check"
Assert-FileContains -Name "Development docs mention demo seed script" -Path "docs/development-environment.md" -Pattern "seed-demo-data\.ps1"
Assert-FileContains -Name "Development docs mention page attachment preflight" -Path "docs/development-environment.md" -Pattern "check-page-attachment-prereqs\.ps1"
Assert-FileContains -Name "MVP-0 runtime verifier updates and deletes category" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "Create update delete category API"
Assert-FileContains -Name "MVP-0 runtime verifier updates and deletes tag" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "Create update delete tag API"
Assert-FileContains -Name "MVP-0 runtime verifier covers position and job level" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "Create and update position API"
Assert-FileContains -Name "MVP-0 runtime verifier covers user assignment" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "Create and update user assignment API"
Assert-FileContains -Name "MVP-0 runtime verifier covers department permission scope" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "Create department permission scope API"
Assert-FileContains -Name "MVP-0 runtime verifier covers position permission scope" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "Create position permission scope API"
Assert-FileContains -Name "MVP-0 runtime verifier covers role permission scope" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "Create role permission scope API"
Assert-FileContains -Name "MVP-0 runtime verifier covers user permission scope" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "Create user permission scope API"
Assert-FileContains -Name "MVP-0 runtime verifier checks audit actions" -Path "ops/verify-mvp0-runtime.ps1" -Pattern "Runtime audit actions written"
Assert-FileContains -Name "MVP-0 acceptance matrix states remaining manual gates" -Path "docs/mvp-0-acceptance-matrix.md" -Pattern "openTabs"
Assert-FileContains -Name "Blazor text forms bind on input" -Path "src/KnowPulse.Blazor.Client/Pages/Knowledge/Tags.razor" -Pattern '@bind:event="oninput"'
Assert-FileContains -Name "Blazor category text forms bind on input" -Path "src/KnowPulse.Blazor.Client/Pages/Knowledge/Categories.razor" -Pattern '@bind:event="oninput"'
Assert-FileContains -Name "Permission scopes page exposes department selector" -Path "src/KnowPulse.Blazor.Client/Pages/Knowledge/PermissionScopes.razor" -Pattern 'id="scope-department"'
Assert-FileContains -Name "Permission scopes page exposes position selector" -Path "src/KnowPulse.Blazor.Client/Pages/Knowledge/PermissionScopes.razor" -Pattern 'id="scope-position"'
Assert-FileContains -Name "Permission scopes page exposes role selector" -Path "src/KnowPulse.Blazor.Client/Pages/Knowledge/PermissionScopes.razor" -Pattern 'id="scope-role"'
Assert-FileContains -Name "Permission scopes page exposes user selector" -Path "src/KnowPulse.Blazor.Client/Pages/Knowledge/PermissionScopes.razor" -Pattern 'id="scope-user"'
Assert-FileContains -Name "Permission scopes page exposes security level" -Path "src/KnowPulse.Blazor.Client/Pages/Knowledge/PermissionScopes.razor" -Pattern 'id="scope-security"'
Assert-FileContains -Name "Knowledge version number uses Razor expression" -Path "src/KnowPulse.Blazor.Client/Pages/Knowledge/Items.razor" -Pattern "v@\(version\.VersionNumber\)"
Add-Check -Name "Knowledge version number does not render Razor literal" -Passed (-not (Test-FileContains -Path "src/KnowPulse.Blazor.Client/Pages/Knowledge/Items.razor" -Pattern "v@version\.VersionNumber")) -Detail "src/KnowPulse.Blazor.Client/Pages/Knowledge/Items.razor"
Assert-FileContains -Name "Permission integration test covers role scope" -Path "test/KnowPulse.EntityFrameworkCore.Tests/EntityFrameworkCore/Applications/Mvp0AppServiceIntegrationTests.cs" -Pattern "KnowledgePermissionScopeType\.Role"
Assert-FileContains -Name "Permission integration test covers user scope" -Path "test/KnowPulse.EntityFrameworkCore.Tests/EntityFrameworkCore/Applications/Mvp0AppServiceIntegrationTests.cs" -Pattern "KnowledgePermissionScopeType\.User"
Assert-FileContains -Name "Permission integration test covers security deny" -Path "test/KnowPulse.EntityFrameworkCore.Tests/EntityFrameworkCore/Applications/Mvp0AppServiceIntegrationTests.cs" -Pattern "MinimumSecurityLevel\s*=\s*999"
Assert-FileContains -Name "Permission integration test covers unauthorized attachment download" -Path "test/KnowPulse.EntityFrameworkCore.Tests/EntityFrameworkCore/Applications/Mvp0AppServiceIntegrationTests.cs" -Pattern "DownloadAttachmentAsync\(hiddenItem\.Id"

$sourceFiles = Get-ChildItem -Path src,test -Recurse -Include *.cs,*.csproj -File
$blockedAiPatterns = "DataIngestion|Microsoft\.Extensions\.VectorData|Microsoft\.Extensions\.AI|SemanticKernel|Qdrant\.Client|Azure\.AI|OpenAI|LiteLLM"
$blockedMatches = @($sourceFiles | Select-String -Pattern $blockedAiPatterns)
if ($blockedMatches.Count -eq 0) {
    $blockedDetail = "No blocked references"
}
else {
    $blockedDetail = ($blockedMatches |
        Select-Object -First 10 |
        ForEach-Object { "{0}:{1}" -f $_.Path, $_.LineNumber }) -join "; "
}

Add-Check -Name "MVP-0 business code has no direct preview RAG/AI provider dependency" -Passed ($blockedMatches.Count -eq 0) -Detail $blockedDetail

$checks | Format-Table -AutoSize

if ($checks | Where-Object { -not $_.Passed }) {
    exit 1
}
