using System;

namespace KnowPulse.Blazor.Client.Pages.Teaching;

internal static class TeacherTime
{
    // 旧业务使用本地 Clock.Now，API 无时区的时间需保留；考试 UTC 时间按本地显示。
    public static string Format(DateTime value) =>
        (value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value).ToString("yyyy-MM-dd HH:mm");

    // ExamAppService 明确使用 UtcNow；关系型数据库读取后 Kind 可能丢失。
    public static string FormatExamUtc(DateTime value) => Format(value.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
        : value);
}
