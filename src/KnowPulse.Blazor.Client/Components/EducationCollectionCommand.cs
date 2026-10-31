using System;
using KnowPulse.Education;

namespace KnowPulse.Blazor.Client.Components;

public sealed class EducationCollectionCommand
{
    public Guid RecipientId { get; set; }
    public SetEducationQuestionCollectionDto Input { get; set; } = new();
    public static string Prefix(Guid? tenant, Guid? account) => $"knowpulse.education.collections.{tenant}.{account}.";
    public string Key(string prefix) => $"{prefix}{Input.SchoolId:D}.{RecipientId:D}.{Input.PaperQuestionId:D}";
    public void Validate()
    {
        if (RecipientId == Guid.Empty || Input == null || Input.SchoolId == Guid.Empty || Input.OperationId == Guid.Empty || Input.PaperQuestionId == Guid.Empty ||
            Input.ExpectedCurrentOperationId == Guid.Empty || Input.ExpectedResultReleaseId == Guid.Empty || Input.IsCollected && !Input.ExpectedResultReleaseId.HasValue)
            throw new InvalidOperationException("Invalid collection operation reference.");
    }
}
