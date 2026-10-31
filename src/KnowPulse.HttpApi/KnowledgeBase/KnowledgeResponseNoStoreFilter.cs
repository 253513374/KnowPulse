using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KnowPulse.HttpApi.KnowledgeBase;

public sealed class KnowledgeResponseNoStoreFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // These services can return owner-private bodies or files, including through conventional routes.
        if (context.Controller is IKnowledgeLibraryAppService or IKnowledgeItemAppService or IKnowledgeManagementAppService or IKnowledgePersonalExportAppService or IEducationNotebookSourceAppService or IPersonalNotebookAppService)
        {
            context.HttpContext.Response.Headers.CacheControl = "private, no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
        }
        await next();
    }
}
