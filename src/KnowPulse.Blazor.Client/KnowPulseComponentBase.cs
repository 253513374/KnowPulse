using KnowPulse.Localization;
using Volo.Abp.AspNetCore.Components;

using System;
using System.Net;
using System.Net.Http;

namespace KnowPulse.Blazor.Client;

public abstract class KnowPulseComponentBase : AbpComponentBase
{
    protected KnowPulseComponentBase()
    {
        LocalizationResource = typeof(KnowPulseResource);
    }

    protected static string GetUserFriendlyErrorMessage(Exception exception)
    {
        var statusCode = TryGetStatusCode(exception);

        if (statusCode == HttpStatusCode.Forbidden)
        {
            return "无权限访问当前资源。请确认账号角色和权限配置。";
        }

        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return "登录状态已失效或未登录，请重新登录。";
        }

        var message = exception.GetBaseException().Message;
        return string.IsNullOrWhiteSpace(message)
            ? "操作失败，请稍后重试。"
            : $"操作失败：{message}";
    }

    protected static HttpStatusCode? TryGetStatusCode(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: { } httpStatusCode })
            {
                return httpStatusCode;
            }

            var reflectedStatusCode = TryGetStatusCodeFromExceptionProperty(current, "StatusCode")
                ?? TryGetStatusCodeFromExceptionProperty(current, "HttpStatusCode");
            if (reflectedStatusCode != null)
            {
                return reflectedStatusCode;
            }

            if (ContainsStatusText(current.Message, "403", "Forbidden"))
            {
                return HttpStatusCode.Forbidden;
            }

            if (ContainsStatusText(current.Message, "401", "Unauthorized"))
            {
                return HttpStatusCode.Unauthorized;
            }
        }

        return null;
    }

    private static HttpStatusCode? TryGetStatusCodeFromExceptionProperty(Exception exception, string propertyName)
    {
        var property = exception.GetType().GetProperty(propertyName);
        var value = property?.GetValue(exception);

        return value switch
        {
            HttpStatusCode statusCode => statusCode,
            int statusCode => Enum.IsDefined(typeof(HttpStatusCode), statusCode)
                ? (HttpStatusCode)statusCode
                : null,
            _ => null
        };
    }

    private static bool ContainsStatusText(string? message, string statusCode, string statusText)
    {
        return !string.IsNullOrWhiteSpace(message)
            && message.Contains(statusCode, StringComparison.OrdinalIgnoreCase)
            && message.Contains(statusText, StringComparison.OrdinalIgnoreCase);
    }
}
