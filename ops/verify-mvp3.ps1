[CmdletBinding()]
param(
    [string]$SolutionPath = ".\KnowPulse.slnx",
    [switch]$NoBuild,
    [switch]$WithBuild,
    [switch]$SkipDotnetTests,
    [switch]$SkipStatic,
    [switch]$SkipBlazorBuild,
    [switch]$SkipRagEvaluation,
    [switch]$SkipAudit,
    [switch]$AuditLocalPsql,
    [switch]$CheckRuntimePages,
    [string]$ApiBaseUrl = "https://localhost:44305",
    [string]$BlazorBaseUrl = "https://localhost:44307"
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

function Get-RepoText {
    param([string]$RelativePath)

    return Get-Content -LiteralPath (Join-Path $repoRoot $RelativePath) -Raw
}

function Test-StaticFile {
    param([string]$RelativePath)

    $exists = Test-Path -LiteralPath (Join-Path $repoRoot $RelativePath)
    Add-Result -Name "Static: $RelativePath exists" -Passed $exists -Detail ($(if ($exists) { "OK" } else { "Missing" }))
}

function Test-StaticContains {
    param(
        [string]$Name,
        [string]$Text,
        [string]$Expected
    )

    $passed = $Text.Contains($Expected)
    Add-Result -Name "Static: $Name" -Passed $passed -Detail ($(if ($passed) { "OK" } else { "Missing '$Expected'" }))
}

function Test-StaticNotMatches {
    param(
        [string]$Name,
        [string]$Text,
        [string]$Pattern
    )

    $passed = -not ($Text -match $Pattern)
    Add-Result -Name "Static: $Name" -Passed $passed -Detail ($(if ($passed) { "OK" } else { "Matched '$Pattern'" }))
}

function Invoke-Mvp3StaticChecks {
    $requiredFiles = @(
        "src\KnowPulse.Domain\Learning\LearningPath.cs",
        "src\KnowPulse.Domain\Learning\LearningPathAssignment.cs",
        "src\KnowPulse.Domain\Learning\LearningPathRequiredKnowledge.cs",
        "src\KnowPulse.Domain\Learning\LearningPathRecommendedKnowledge.cs",
        "src\KnowPulse.Domain\Learning\LearningPathRequiredExam.cs",
        "src\KnowPulse.Domain\Learning\LearningKnowledgeReadRecord.cs",
        "src\KnowPulse.Domain\KnowledgeBase\KnowledgeFeedback.cs",
        "src\KnowPulse.Domain\RagAI\RagEvaluationCase.cs",
        "src\KnowPulse.Domain\RagAI\RagEvaluationRun.cs",
        "src\KnowPulse.Domain\RagAI\RagEvaluationRunCaseResult.cs",
        "src\KnowPulse.Application.Contracts\Learning\ILearningPathAppService.cs",
        "src\KnowPulse.Application.Contracts\Learning\LearningDtos.cs",
        "src\KnowPulse.Application.Contracts\Learning\LearningInputs.cs",
        "src\KnowPulse.Application\Learning\LearningPathAppService.cs",
        "src\KnowPulse.Application.Contracts\KnowledgeBase\IKnowledgeFeedbackAppService.cs",
        "src\KnowPulse.Application.Contracts\KnowledgeBase\KnowledgeFeedbackDto.cs",
        "src\KnowPulse.Application.Contracts\KnowledgeBase\KnowledgeFeedbackInputs.cs",
        "src\KnowPulse.Application\KnowledgeBase\KnowledgeFeedbackAppService.cs",
        "src\KnowPulse.Application.Contracts\RagAI\RagEvaluationContracts.cs",
        "src\KnowPulse.Application\RagAI\RagEvaluationAppService.cs",
        "src\KnowPulse.HttpApi\Controllers\Learning\LearningPathsController.cs",
        "src\KnowPulse.HttpApi\Controllers\KnowledgeBase\KnowledgeFeedbackController.cs",
        "src\KnowPulse.HttpApi\Controllers\RagAI\RagEvaluationsController.cs",
        "src\KnowPulse.Blazor.Client\Pages\Learning\MyLearning.razor",
        "src\KnowPulse.Blazor.Client\Pages\Knowledge\Feedback.razor",
        "test\KnowPulse.Domain.Tests\Learning\LearningPathTests.cs",
        "test\KnowPulse.Domain.Tests\KnowledgeBase\KnowledgeFeedbackTests.cs",
        "test\KnowPulse.Domain.Tests\RagAI\RagEvaluationTests.cs",
        "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp3LearningPathAppServiceTests.cs",
        "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp3KnowledgeFeedbackAppServiceTests.cs",
        "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp1RagEvaluationAppServiceTests.cs",
        "test\KnowPulse.Application.Tests\RagAI\RagEvaluationApiContractTests.cs",
        "docs\mvp-3-learning-path-acceptance.md",
        "docs\mvp-3-knowledge-feedback-acceptance.md",
        "docs\mvp-3-acceptance-matrix.md",
        "ops\verify-rag-evaluation.ps1"
    )

    foreach ($file in $requiredFiles) {
        Test-StaticFile $file
    }

    foreach ($migrationName in @("Mvp3LearningPath", "Mvp3LearningCenter", "Mvp3KnowledgeFeedback", "Mvp1RagEvaluation")) {
        $migration = Get-ChildItem -LiteralPath (Join-Path $repoRoot "src\KnowPulse.EntityFrameworkCore\Migrations") -Filter "*_$migrationName.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
        Add-Result -Name "Static: $migrationName migration exists" -Passed ($null -ne $migration) -Detail ($(if ($migration) { $migration.Name } else { "Missing" }))
    }

    $dbContext = Get-RepoText "src\KnowPulse.EntityFrameworkCore\EntityFrameworkCore\KnowPulseDbContext.cs"
    foreach ($expected in @(
        "DbSet<LearningPath>",
        "DbSet<LearningPathAssignment>",
        "DbSet<LearningPathRequiredKnowledge>",
        "DbSet<LearningPathRecommendedKnowledge>",
        "DbSet<LearningPathRequiredExam>",
        "DbSet<LearningKnowledgeReadRecord>",
        "DbSet<KnowledgeFeedback>",
        "DbSet<KnowPulse.RagAI.RagEvaluationCase>",
        "DbSet<KnowPulse.RagAI.RagEvaluationRun>",
        "DbSet<KnowPulse.RagAI.RagEvaluationRunCaseResult>",
        "LearningKnowledgeReadRecords",
        "KnowledgeFeedbacks",
        "RagEvaluationRunCaseResults"
    )) {
        Test-StaticContains "DbContext contains $expected" $dbContext $expected
    }

    $permissions = Get-RepoText "src\KnowPulse.Application.Contracts\Permissions\KnowPulsePermissions.cs"
    foreach ($expected in @(
        "public static class Learning",
        "GroupName + `".Learning`"",
        "public const string Manage = Default + `".Manage`"",
        "public const string View = Default + `".View`"",
        "public const string Feedback = Default + `".Feedback`"",
        "public const string Evaluations = Default + `".Evaluations`""
    )) {
        Test-StaticContains "permission constants contain $expected" $permissions $expected
    }

    $permissionProvider = Get-RepoText "src\KnowPulse.Application.Contracts\Permissions\KnowPulsePermissionDefinitionProvider.cs"
    foreach ($expected in @(
        "Permission:Learning.Manage",
        "Permission:Learning.View",
        "Permission:KnowledgeBase.Feedback",
        "Permission:Rag.Evaluations"
    )) {
        Test-StaticContains "permission provider contains $expected" $permissionProvider $expected
    }

    $menu = Get-RepoText "src\KnowPulse.Blazor.Client\Menus\KnowPulseMenuContributor.cs"
    foreach ($expected in @(
        "/learning/my",
        "KnowPulsePermissions.Learning.View",
        "/knowledge/feedback",
        "KnowPulsePermissions.KnowledgeBase.Feedback"
    )) {
        Test-StaticContains "menu contains $expected" $menu $expected
    }

    $learningAppService = Get-RepoText "src\KnowPulse.Application\Learning\LearningPathAppService.cs"
    foreach ($expected in @(
        "CheckMvpPermissionAsync(KnowPulsePermissions.Learning.Manage",
        "CheckMvpPermissionAsync(KnowPulsePermissions.Learning.View",
        "IsAssignedAsync",
        "UserOrganizationAssignment",
        "KnowledgeAccessEvaluator.CanAccess",
        "LearningKnowledgeReadRecord",
        "MarkKnowledgeReadAsync",
        "GetExamCompletionStatus"
    )) {
        Test-StaticContains "learning app service contains $expected" $learningAppService $expected
    }

    $feedbackAppService = Get-RepoText "src\KnowPulse.Application\KnowledgeBase\KnowledgeFeedbackAppService.cs"
    foreach ($expected in @(
        "CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items",
        "CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Feedback",
        "KnowledgeAccessEvaluator.CanAccess",
        "EnsureLinkedVersionBelongsToFeedbackItemAsync",
        "feedback.Resolve",
        "feedback.Close",
        "feedback.LinkKnowledgeVersion"
    )) {
        Test-StaticContains "feedback app service contains $expected" $feedbackAppService $expected
    }
    Test-StaticNotMatches "feedback service does not write knowledge versions directly" $feedbackAppService "_versionRepository\.(Insert|Update|Delete)Async"
    Test-StaticNotMatches "feedback service does not publish versions directly" $feedbackAppService "\.Publish\("

    $ragEvaluation = Get-RepoText "src\KnowPulse.Application\RagAI\RagEvaluationAppService.cs"
    foreach ($expected in @(
        "ExpectedRefusal",
        "ForbiddenCitations",
        "UnauthorizedRecallRate",
        "unauthorizedRecallCount > 0",
        "HardFailed",
        "ValidatePermissionContext",
        "KnowledgeAccessContext"
    )) {
        Test-StaticContains "RAG evaluation app service contains $expected" $ragEvaluation $expected
    }

    $myLearningPage = Get-RepoText "src\KnowPulse.Blazor.Client\Pages\Learning\MyLearning.razor"
    foreach ($expected in @(
        "@page `"/learning/my`"",
        "Authorize(KnowPulsePermissions.Learning.View)",
        "加载中",
        "暂无分配给你的学习路径",
        "无权限访问学习中心",
        "去阅读",
        "标记完成",
        "MarkKnowledgeReadAsync",
        "NavigationManager.NavigateTo($`"/knowledge/items?itemId=",
        "NavigationManager.NavigateTo(`"/exam/take`")"
    )) {
        Test-StaticContains "MyLearning page contains $expected" $myLearningPage $expected
    }

    $feedbackPage = Get-RepoText "src\KnowPulse.Blazor.Client\Pages\Knowledge\Feedback.razor"
    foreach ($expected in @(
        "@page `"/knowledge/feedback`"",
        "Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)",
        "加载中",
        "暂无知识反馈",
        "无权限处理知识反馈",
        "关联修订版本 ID",
        "Guid.TryParse",
        "ProcessAsync",
        "LinkVersionAsync",
        "ResolveAsync",
        "CloseAsync"
    )) {
        Test-StaticContains "feedback page contains $expected" $feedbackPage $expected
    }

    $auditScript = Get-RepoText "ops\audit-check.ps1"
    foreach ($expected in @(
        "Knowledge feedback trail",
        "Knowledge feedback sensitive field scan",
        "RAG sensitive field scan",
        "FailOnSensitiveHits"
    )) {
        Test-StaticContains "audit script contains $expected" $auditScript $expected
    }

    foreach ($testSpec in @(
        @{ Path = "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp3LearningPathAppServiceTests.cs"; Expected = "My_List_Should_Only_Return_Active_Paths_Assigned_By_Department_Position_Or_JobLevel" },
        @{ Path = "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp3LearningPathAppServiceTests.cs"; Expected = "My_List_Should_Filter_Knowledge_Content_By_Knowledge_Permission_Scopes" },
        @{ Path = "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp3LearningPathAppServiceTests.cs"; Expected = "My_Learning_Should_Record_Read_And_Completion_Status" },
        @{ Path = "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp3KnowledgeFeedbackAppServiceTests.cs"; Expected = "Should_Submit_Feedback_Without_Modifying_Published_Version" },
        @{ Path = "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp3KnowledgeFeedbackAppServiceTests.cs"; Expected = "Should_Process_Link_Resolve_And_Close_Feedback" },
        @{ Path = "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp1RagEvaluationAppServiceTests.cs"; Expected = "RunAsync_Should_Hard_Fail_When_Unauthorized_Recall_Detected" },
        @{ Path = "test\KnowPulse.Application.Tests\RagAI\RagEvaluationApiContractTests.cs"; Expected = "Controller_Should_Expose_Case_And_Run_Endpoints" }
    )) {
        $testText = Get-RepoText $testSpec.Path
        Test-StaticContains "test $($testSpec.Path) contains $($testSpec.Expected)" $testText $testSpec.Expected
    }

    $staticFailures = @($results | Where-Object { $_.Name.StartsWith("Static:") -and -not $_.Passed })
    if ($staticFailures.Count -gt 0) {
        throw "Static MVP-3 checks failed: $($staticFailures.Count)."
    }
}

function Invoke-UrlCheck {
    param(
        [string]$Name,
        [string]$Url
    )

    $requestArgs = @{
        Uri = $Url
        Method = "GET"
        UseBasicParsing = $true
        TimeoutSec = 20
    }

    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $requestArgs["SkipCertificateCheck"] = $true
    }

    $response = Invoke-WebRequest @requestArgs
    if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 400) {
        throw "$Name returned HTTP $($response.StatusCode)."
    }

    Write-Host "$Name HTTP $($response.StatusCode): $Url"
}

Push-Location $repoRoot
try {
    if ($WithBuild) {
        Invoke-VerifyStep -Name "dotnet build" -Action {
            & dotnet build $SolutionPath --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false -p:UseSharedCompilation=false
        }
    }

    if (-not $SkipStatic) {
        Invoke-VerifyStep -Name "MVP-3 static acceptance checks" -Action {
            Invoke-Mvp3StaticChecks
        }
    }

    if (-not $SkipDotnetTests) {
        Invoke-VerifyStep -Name "MVP-3 domain tests" -Action {
            $testArgs = @(
                "test",
                ".\test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj",
                "--no-restore",
                "--filter",
                "FullyQualifiedName~LearningPathTests|FullyQualifiedName~KnowledgeFeedbackTests",
                "-v:m",
                "-m:1",
                "-p:UseSharedCompilation=false",
                "-p:BuildInParallel=false",
                "-nr:false"
            )

            if ($NoBuild) {
                $testArgs = $testArgs + "--no-build"
            }

            & dotnet @testArgs
        }

        Invoke-VerifyStep -Name "MVP-3 EF application service tests" -Action {
            $testArgs = @(
                "test",
                ".\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj",
                "--no-restore",
                "--filter",
                "FullyQualifiedName~Mvp3LearningPathAppServiceTests|FullyQualifiedName~Mvp3KnowledgeFeedbackAppServiceTests",
                "-v:m",
                "-m:1",
                "-p:UseSharedCompilation=false",
                "-p:BuildInParallel=false",
                "-nr:false"
            )

            if ($NoBuild) {
                $testArgs = $testArgs + "--no-build"
            }

            & dotnet @testArgs
        }
    }

    if (-not $SkipRagEvaluation) {
        Invoke-VerifyStep -Name "RAG evaluation verification" -Action {
            if ($NoBuild) {
                & .\ops\verify-rag-evaluation.ps1 -NoBuild
            }
            else {
                & .\ops\verify-rag-evaluation.ps1
            }
        }
    }

    if (-not $SkipBlazorBuild) {
        Invoke-VerifyStep -Name "Blazor Client build" -Action {
            & dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -v:m -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false
        }
    }

    if (-not $SkipAudit) {
        Invoke-VerifyStep -Name "Audit sensitive field check" -Action {
            if ($AuditLocalPsql) {
                & .\ops\audit-check.ps1 -FailOnSensitiveHits -Limit 10 -LocalPsql
            }
            else {
                & .\ops\audit-check.ps1 -FailOnSensitiveHits -Limit 10
            }
        }
    }

    if ($CheckRuntimePages) {
        Invoke-VerifyStep -Name "Runtime page smoke checks" -Action {
            Invoke-UrlCheck -Name "Swagger" -Url "$ApiBaseUrl/swagger/index.html"
            Invoke-UrlCheck -Name "My learning page" -Url "$BlazorBaseUrl/learning/my"
            Invoke-UrlCheck -Name "Knowledge feedback page" -Url "$BlazorBaseUrl/knowledge/feedback"
            Invoke-UrlCheck -Name "Blazor runtime asset" -Url "$BlazorBaseUrl/_framework/dotnet.js"
        }
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "MVP-3 verification summary"
$results | Format-Table -AutoSize

if ($results | Where-Object { -not $_.Passed }) {
    exit 1
}
