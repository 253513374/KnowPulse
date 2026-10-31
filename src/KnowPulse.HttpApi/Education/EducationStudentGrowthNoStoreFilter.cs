using System.Threading.Tasks;
using KnowPulse.Education;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KnowPulse.HttpApi.Education;

public sealed class EducationStudentGrowthNoStoreFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Controller is IEducationStudentGrowthAppService or IEducationTeachingPlanAppService or IEducationTeachingPlanExecutionAppService or IEducationAssessmentEvidenceAppService or IEducationTeachingPlanActivityAppService or IEducationTeachingFormalAssessmentAppService or IEducationStudyTaskAppService or IEducationStudyHintReviewAppService or IEducationCorrectionAppService or IEducationLearningWorkloadAppService or IEducationQuestionPracticeAppService or IEducationSchoolAppService or IEducationFeedbackAppService or IEducationLearningReportAppService or IEducationQuestionCollectionAppService or IEducationAssessmentReminderAppService or IEducationStudentInboxAppService)
        {
            context.HttpContext.Response.Headers.CacheControl = "private, no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
        }
        await next();
    }
}
