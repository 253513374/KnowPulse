using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KnowPulse.Education;

public static class EducationCorrectionFingerprint
{
    private static string Hash(object input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
    public static string Draft(Guid taskId, SaveEducationCorrectionDraftDto input) => Hash(new { Command = "correction-draft", taskId, input });
    public static string Publish(Guid taskId, PublishEducationCorrectionDto input) => Hash(new { Command = "correction-publish", taskId, input });
    public static string Withdraw(Guid taskId, WithdrawEducationCorrectionDto input) => Hash(new { Command = "correction-withdraw", taskId, input });
    public static string Submit(Guid deliveryId, SubmitEducationCorrectionDto input) => Hash(new { Command = "correction-submit", deliveryId, input });
    public static string Review(Guid deliveryId, PublishEducationCorrectionReviewDto input) => Hash(new { Command = "correction-review", deliveryId, input });
}
