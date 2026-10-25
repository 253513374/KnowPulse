using System.Collections.Generic;
using System.Linq;
using KnowPulse.QuestionBank;

namespace KnowPulse.Blazor.Client.Pages.Teaching;

internal static class TeacherAnswer
{
    public static string Format(QuestionType type, IEnumerable<string> values) => string.Join("、",
        values.Select(value => type == QuestionType.TrueFalse
            ? value.ToLowerInvariant() switch { "true" => "正确", "false" => "错误", _ => value }
            : value));
}
