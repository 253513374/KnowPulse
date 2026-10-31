using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KnowPulse.Education;

public static class EducationPracticeFingerprint
{
    private static string Hash(object input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
    public static string Start(Guid recipientId, StartEducationPracticeDto input) => Hash(new { Command = "start-practice", recipientId, input });
    public static string Submit(Guid id, SubmitEducationPracticeDto input) => Hash(new { Command = "submit-practice", id, input });
}
