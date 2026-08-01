[CmdletBinding()]
param(
    [string]$SolutionPath = ".\KnowPulse.slnx",
    [string]$Mvp2TestFilter = "FullyQualifiedName~Mvp2",
    [switch]$NoBuild,
    [switch]$WithBuild,
    [switch]$SkipDotnetTests,
    [switch]$SkipStatic,
    [switch]$SkipAudit,
    [switch]$AuditLocalPsql,
    [switch]$SeedDemoData,
    [switch]$CheckExamRuntime,
    [string]$ApiBaseUrl = "https://localhost:44305",
    [string]$DemoPlanId = "",
    [string]$DemoPlanNameLike = "%MVP2-DEMO%",
    [string]$Database = "KnowPulse",
    [string]$PostgresUser = "root",
    [string]$PostgresPassword = "myPassword",
    [string]$PostgresHost = "localhost",
    [int]$PostgresPort = 5432
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

function ConvertTo-SqlLiteral {
    param([string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

function Invoke-PostgresScalar {
    param([string]$Sql)

    if ($AuditLocalPsql) {
        $oldPassword = $env:PGPASSWORD
        $env:PGPASSWORD = $PostgresPassword
        try {
            $output = & psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $Database -v ON_ERROR_STOP=1 -t -A -c $Sql
        }
        finally {
            $env:PGPASSWORD = $oldPassword
        }
    }
    else {
        $output = & docker compose exec -T postgres psql -U $PostgresUser -d $Database -v ON_ERROR_STOP=1 -t -A -c $Sql
    }

    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL scalar query failed with exit code $LASTEXITCODE."
    }

    $lines = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -eq 0) {
        return ""
    }

    return $lines[-1].Trim()
}

function Invoke-Mvp2StaticChecks {
    $requiredFiles = @(
        "src\KnowPulse.Domain\QuestionBank\Question.cs",
        "src\KnowPulse.Domain\QuestionBank\QuestionOption.cs",
        "src\KnowPulse.Domain\QuestionBank\QuestionAnswer.cs",
        "src\KnowPulse.Domain\QuestionBank\QuestionReview.cs",
        "src\KnowPulse.Domain\QuestionBank\QuestionSource.cs",
        "src\KnowPulse.Domain\QuestionBank\QuestionSimilarityCheck.cs",
        "src\KnowPulse.Application.Contracts\QuestionBank\IQuestionAppService.cs",
        "src\KnowPulse.Application.Contracts\QuestionBank\IQuestionWorkflowAppService.cs",
        "src\KnowPulse.Application.Contracts\QuestionBank\QuestionDtos.cs",
        "src\KnowPulse.Application\QuestionBank\QuestionAppService.cs",
        "src\KnowPulse.Domain\AiQuestioning\AiQuestionGenerationTask.cs",
        "src\KnowPulse.Domain\AiQuestioning\AiQuestionCandidate.cs",
        "src\KnowPulse.Domain\AiQuestioning\AiQuestionCandidateCitation.cs",
        "src\KnowPulse.Domain\AiQuestioning\AiQuestionModelCallLog.cs",
        "src\KnowPulse.Application.Contracts\AiQuestioning\IAiQuestionGenerationAppService.cs",
        "src\KnowPulse.Application.Contracts\AiQuestioning\AiQuestionGenerationDtos.cs",
        "src\KnowPulse.Application\AiQuestioning\AiQuestionGenerationAppService.cs",
        "src\KnowPulse.Application\AiQuestioning\QuestionSimilarityService.cs",
        "src\KnowPulse.HttpApi\Controllers\QuestionBank\QuestionsController.cs",
        "src\KnowPulse.HttpApi\Controllers\AiQuestioning\AiQuestionGenerationController.cs",
        "src\KnowPulse.Domain\Exam\ExamPlan.cs",
        "src\KnowPulse.Domain\Exam\ExamPaper.cs",
        "src\KnowPulse.Domain\Exam\ExamPaperQuestion.cs",
        "src\KnowPulse.Domain\Exam\ExamAssignment.cs",
        "src\KnowPulse.Domain\Exam\ExamAttempt.cs",
        "src\KnowPulse.Domain\Exam\ExamAnswer.cs",
        "src\KnowPulse.Domain\Exam\ExamScore.cs",
        "src\KnowPulse.Domain\Exam\WrongQuestionRecord.cs",
        "src\KnowPulse.Application.Contracts\Exam\IExamAppService.cs",
        "src\KnowPulse.Application.Contracts\Exam\ExamDtos.cs",
        "src\KnowPulse.Application.Contracts\Exam\ExamInputs.cs",
        "src\KnowPulse.Application\Exam\ExamAppService.cs",
        "src\KnowPulse.Blazor.Client\Pages\Exam\Management.razor",
        "src\KnowPulse.Blazor.Client\Pages\Exam\Take.razor",
        "src\KnowPulse.Blazor.Client\Pages\QuestionBank\Questions.razor",
        "src\KnowPulse.Blazor.Client\Pages\QuestionBank\QuestionDrafts.razor",
        "src\KnowPulse.Blazor.Client\Pages\QuestionBank\QuestionReviewQueue.razor",
        "src\KnowPulse.Blazor.Client\Pages\QuestionBank\FormalQuestions.razor",
        "src\KnowPulse.Blazor.Client\Pages\QuestionBank\QuestionEditor.razor",
        "src\KnowPulse.Blazor.Client\Pages\QuestionBank\QuestionReview.razor",
        "src\KnowPulse.Blazor.Client\Pages\QuestionBank\AiCandidates.razor",
        "test\KnowPulse.Domain.Tests\QuestionBank\QuestionTests.cs",
        "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp2QuestionBankAppServiceTests.cs",
        "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp2AiQuestionGenerationAppServiceTests.cs",
        "test\KnowPulse.Application.Tests\AiQuestioning\AiQuestionGenerationApiContractTests.cs",
        "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp2ExamAppServiceTests.cs",
        "ops\seed-mvp2-demo-data.ps1"
    )

    foreach ($file in $requiredFiles) {
        Test-StaticFile $file
    }

    $migration = Get-ChildItem -LiteralPath (Join-Path $repoRoot "src\KnowPulse.EntityFrameworkCore\Migrations") -Filter "*_Mvp2ExamLoop.cs" -ErrorAction SilentlyContinue | Select-Object -First 1
    Add-Result -Name "Static: Mvp2ExamLoop migration exists" -Passed ($null -ne $migration) -Detail ($(if ($migration) { $migration.Name } else { "Missing" }))

    $dbContext = Get-RepoText "src\KnowPulse.EntityFrameworkCore\EntityFrameworkCore\KnowPulseDbContext.cs"
    foreach ($expected in @(
        "DbSet<AiQuestionPromptTemplate>",
        "DbSet<AiQuestionGenerationTask>",
        "DbSet<AiQuestionCandidate>",
        "DbSet<AiQuestionCandidateCitation>",
        "DbSet<AiQuestionModelCallLog>",
        "DbSet<Question>",
        "DbSet<QuestionOption>",
        "DbSet<QuestionAnswer>",
        "DbSet<QuestionAnalysis>",
        "DbSet<QuestionKnowledgePoint>",
        "DbSet<QuestionSource>",
        "DbSet<QuestionReview>",
        "DbSet<QuestionSimilarityCheck>",
        "AiQuestionModelCallLogs",
        "QuestionReviews",
        "QuestionSimilarityChecks",
        "DbSet<ExamPlan>",
        "DbSet<ExamPaper>",
        "DbSet<ExamPaperQuestion>",
        "DbSet<ExamAssignment>",
        "DbSet<ExamAttempt>",
        "DbSet<ExamAnswer>",
        "DbSet<ExamScore>",
        "DbSet<WrongQuestionRecord>",
        "ExamAttempts",
        "ExamAnswers",
        "ExamScores",
        "WrongQuestionRecords"
    )) {
        Test-StaticContains "DbContext contains $expected" $dbContext $expected
    }

    $permissions = Get-RepoText "src\KnowPulse.Application.Contracts\Permissions\KnowPulsePermissions.cs"
    foreach ($expected in @(
        "public static class QuestionBank",
        "Default = GroupName + `".QuestionBank`"",
        "Questions = Default + `".Questions`"",
        "Reviews = Default + `".Reviews`"",
        "AiGeneration = Default + `".AiGeneration`"",
        "public static class Exam",
        "Default = GroupName + `".Exam`"",
        "Manage = Default + `".Manage`"",
        "Participate = Default + `".Participate`"",
        "Results = Default + `".Results`""
    )) {
        Test-StaticContains "Exam permission constants contain $expected" $permissions $expected
    }

    $permissionProvider = Get-RepoText "src\KnowPulse.Application.Contracts\Permissions\KnowPulsePermissionDefinitionProvider.cs"
    foreach ($expected in @(
        "Permission:QuestionBank.Questions",
        "Permission:QuestionBank.Reviews",
        "Permission:QuestionBank.AiGeneration",
        "Permission:Exam.Manage",
        "Permission:Exam.Participate",
        "Permission:Exam.Results"
    )) {
        Test-StaticContains "Exam permission provider contains $expected" $permissionProvider $expected
    }

    $questionAppService = Get-RepoText "src\KnowPulse.Application\QuestionBank\QuestionAppService.cs"
    foreach ($expected in @(
        "CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions",
        "CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Reviews",
        "GetFormalListAsync",
        "SubmitReviewAsync",
        "ApproveAsync",
        "RejectAsync",
        "ReturnForRevisionAsync",
        "DisableAsync",
        "ArchiveAsync",
        "CreateManualDraftAsync",
        "AdoptAiCandidateAsync",
        "DecideReviewAsync",
        "QuestionReviewCommentRequired",
        "QuestionWorklistKind.ReviewQueue",
        "QuestionStatus.Approved",
        "OnlyUsableForExam",
        "AddReviewAsync"
    )) {
        Test-StaticContains "Question app service contains $expected" $questionAppService $expected
    }

    $aiAppService = Get-RepoText "src\KnowPulse.Application\AiQuestioning\AiQuestionGenerationAppService.cs"
    foreach ($expected in @(
        "CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration",
        "_knowledgeRetriever.SearchAsync",
        "RagCallStatus.NoContext",
        "AdoptCandidateAsDraftAsync",
        "_questionWorkflowAppService.AdoptAiCandidateAsync",
        "AiQuestionCandidateCitation",
        "AiQuestionModelCallLog",
        "MatchCitations",
        "ValidateCandidate",
        "ContainsUnsafeLeakage",
        "ComputeHash"
    )) {
        Test-StaticContains "AI question app service contains $expected" $aiAppService $expected
    }

    $appService = Get-RepoText "src\KnowPulse.Application\Exam\ExamAppService.cs"
    foreach ($expected in @(
        "CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Manage",
        "CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Participate",
        "CheckMvpPermissionAsync(KnowPulsePermissions.Exam.Results",
        "PreviewPaperAsync",
        "CreatePlanAsync",
        "GetMyPlanListAsync",
        "StartAttemptAsync",
        "SaveAnswersAsync",
        "SubmitAttemptAsync",
        "GradeAttemptAsync",
        "QuestionStatus.Approved",
        "CorrectAnswersJson",
        "WrongQuestionRecord",
        "ScoringVersion"
    )) {
        Test-StaticContains "Exam app service contains $expected" $appService $expected
    }

    $attempt = Get-RepoText "src\KnowPulse.Domain\Exam\ExamAttempt.cs"
    foreach ($expected in @(
        "SubmitIdempotencyKey",
        "ExamAttemptStatus.InProgress",
        "ExamAttemptStatus.Submitted",
        "ExamAttemptStatus.Scored",
        "ExamAttemptCannotSave",
        "ExamAttemptCannotSubmit"
    )) {
        Test-StaticContains "Exam attempt domain contains $expected" $attempt $expected
    }

    $paperQuestion = Get-RepoText "src\KnowPulse.Domain\Exam\ExamPaperQuestion.cs"
    foreach ($expected in @(
        "OriginalQuestionId",
        "Stem",
        "OptionsJson",
        "CorrectAnswersJson",
        "Analysis",
        "KnowledgePointsSnapshot",
        "SourceSnapshot"
    )) {
        Test-StaticContains "Paper question snapshot contains $expected" $paperQuestion $expected
    }

    $managementPage = Get-RepoText "src\KnowPulse.Blazor.Client\Pages\Exam\Management.razor"
    foreach ($expected in @(
        "@page `"/exam/management`"",
        "Authorize(KnowPulsePermissions.Exam.Manage)",
        "组卷规则",
        "分配范围",
        "预览试卷",
        "发布考试"
    )) {
        Test-StaticContains "Exam management page contains $expected" $managementPage $expected
    }

    $takePage = Get-RepoText "src\KnowPulse.Blazor.Client\Pages\Exam\Take.razor"
    foreach ($expected in @(
        "@page `"/exam/take`"",
        "Authorize(KnowPulsePermissions.Exam.Participate)",
        "剩余",
        "SaveQuestionAsync",
        "RetrySaveAsync",
        "手动保存",
        "提交试卷",
        "disabled=`"@(IsBusy || IsSubmitted)`""
    )) {
        Test-StaticContains "Exam take page contains $expected" $takePage $expected
    }

    $tests = Get-RepoText "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp2ExamAppServiceTests.cs"
    foreach ($expected in @(
        "Paper_Generation_Should_Filter_Status_And_Preserve_Question_Snapshot",
        "Fixed_Paper_Generation_Should_Reject_Not_Approved_Questions",
        "Attempt_Should_Start_Save_Submit_And_Keep_Submit_Idempotent",
        "Scoring_Should_Record_Wrong_Questions_And_Remain_Idempotent",
        "Expired_Attempt_Should_Reject_Save_And_Submit"
    )) {
        Test-StaticContains "Exam test contains $expected" $tests $expected
    }

    $questionTests = Get-RepoText "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp2QuestionBankAppServiceTests.cs"
    foreach ($expected in @(
        "Review_Flow_Should_Require_Submission_And_Record_Audit_Trail",
        "Returned_Question_Should_Be_Editable_And_Submittable_Again",
        "OnlyUsableForExam"
    )) {
        Test-StaticContains "Question bank test contains $expected" $questionTests $expected
    }

    $aiTests = Get-RepoText "test\KnowPulse.EntityFrameworkCore.Tests\EntityFrameworkCore\Applications\Mvp2AiQuestionGenerationAppServiceTests.cs"
    foreach ($expected in @(
        "CreateAsync_Should_Generate_Candidate_With_Traceable_Citation_And_Adopt_As_Draft",
        "Qa002_Citation_Should_Be_Traceable_And_Not_Copy_Full_Knowledge_Content",
        "CreateAsync_Should_Not_Use_Unauthorized_Vector_Hits",
        "CreateAsync_Should_Keep_Invalid_Model_Output_As_Unsubmittable_Candidate",
        "CreateAsync_Should_Record_Similar_Question_Warnings"
    )) {
        Test-StaticContains "AI question generation test contains $expected" $aiTests $expected
    }

    $staticFailures = @($results | Where-Object { $_.Name.StartsWith("Static:") -and -not $_.Passed })
    if ($staticFailures.Count -gt 0) {
        throw "Static MVP-2 checks failed: $($staticFailures.Count)."
    }
}

function Invoke-ExamRuntimeCheck {
    $whereClause = if (-not [string]::IsNullOrWhiteSpace($DemoPlanId)) {
        "p.""Id"" = $(ConvertTo-SqlLiteral $DemoPlanId)"
    }
    else {
        "p.""Name"" ILIKE $(ConvertTo-SqlLiteral $DemoPlanNameLike)"
    }

    $sql = @"
WITH exam_state AS (
    SELECT
        COUNT(DISTINCT p."Id") AS plans,
        COUNT(DISTINCT pq."Id") AS paper_questions,
        COUNT(DISTINCT a."Id") AS scored_attempts,
        COUNT(DISTINCT ans."Id") AS answers,
        COUNT(DISTINCT s."Id") AS scores
    FROM "AppExamPlans" p
    JOIN "AppExamPapers" paper ON paper."Id" = p."PaperId"
    JOIN "AppExamPaperQuestions" pq ON pq."PaperId" = paper."Id"
    LEFT JOIN "AppExamAttempts" a ON a."PlanId" = p."Id" AND a."Status" = 3
    LEFT JOIN "AppExamAnswers" ans ON ans."AttemptId" = a."Id"
    LEFT JOIN "AppExamScores" s ON s."AttemptId" = a."Id"
    WHERE $whereClause
)
SELECT CASE
    WHEN plans > 0 AND paper_questions > 0 AND scored_attempts > 0 AND answers > 0 AND scores > 0
    THEN 'OK plans=' || plans || ' paper_questions=' || paper_questions || ' scored_attempts=' || scored_attempts || ' answers=' || answers || ' scores=' || scores
    ELSE 'FAIL plans=' || plans || ' paper_questions=' || paper_questions || ' scored_attempts=' || scored_attempts || ' answers=' || answers || ' scores=' || scores
END
FROM exam_state;
"@

    $result = Invoke-PostgresScalar $sql
    if (-not $result.StartsWith("OK ")) {
        throw $result
    }

    Write-Host $result
}

Push-Location $repoRoot
try {
    if ($WithBuild) {
        Invoke-VerifyStep -Name "dotnet build" -Action {
            & dotnet build $SolutionPath --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false -p:UseAppHost=false
        }
    }

    if (-not $SkipStatic) {
        Invoke-VerifyStep -Name "MVP-2 static scope checks" -Action {
            Invoke-Mvp2StaticChecks
        }
    }

    if (-not $SkipDotnetTests) {
        Invoke-VerifyStep -Name "MVP-2 filtered tests" -Action {
            $testArgs = @(
                "test",
                $SolutionPath,
                "--no-restore",
                "--filter",
                $Mvp2TestFilter,
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
                    $Mvp2TestFilter,
                    "-v:m",
                    "-m:1",
                    "-nr:false"
                )
            }

            & dotnet @testArgs
        }
    }

    if ($SeedDemoData) {
        Invoke-VerifyStep -Name "MVP-2 demo seed" -Action {
            & .\ops\seed-mvp2-demo-data.ps1 -ApiBaseUrl $ApiBaseUrl
        }
    }

    if ($CheckExamRuntime) {
        Invoke-VerifyStep -Name "MVP-2 runtime exam closure check" -Action {
            Invoke-ExamRuntimeCheck
        }
    }

    if (-not $SkipAudit) {
        Invoke-VerifyStep -Name "Audit sensitive field check" -Action {
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
Write-Host "MVP-2 verification summary"
$results | Format-Table -AutoSize

if ($results | Where-Object { -not $_.Passed }) {
    exit 1
}
