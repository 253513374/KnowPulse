using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KnowPulse.Education;

public static class EducationStudyTaskFingerprint
{
    private static string Hash(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static string Draft(Guid taskId, SaveEducationStudyTaskDto input) => Hash(new { Command = "study-draft", taskId, input });
    public static string Publish(Guid taskId, PublishEducationStudyTaskDto input) => Hash(new { Command = "study-publish", taskId, input });
    public static string Withdraw(Guid taskId, WithdrawEducationStudyTaskDto input) => Hash(new { Command = "study-withdraw", taskId, input });
    public static string Progress(Guid recipientId, SaveEducationMyActivityDto input) => Hash(new { Command = "study-progress", recipientId, input });
    public static string FollowUp(Guid recipientId, SaveEducationStudyFollowUpDto input) => Hash(new { Command = "study-follow-up", recipientId, input });
    public static string HintReview(Guid recipientId, Guid paperQuestionId, SaveEducationHintReviewDto input) => Hash(new { Command = "study-hint-review", recipientId, paperQuestionId, input });
}
