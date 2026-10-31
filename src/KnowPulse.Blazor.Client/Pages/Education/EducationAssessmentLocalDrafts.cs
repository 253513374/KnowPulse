using System;
using System.Collections.Generic;
using System.Linq;
using KnowPulse.Education;
using KnowPulse.QuestionBank;

namespace KnowPulse.Blazor.Client.Pages.Education;

internal sealed class EducationAssessmentLocalDrafts
{
    public Dictionary<Guid, EducationAssessmentResponseValue> Values { get; set; } = [];
    public Dictionary<Guid, Guid?> BaseRevisionIds { get; set; } = [];

    public EducationAssessmentResponseValue Value(EducationStudentAssessmentQuestionDto question, EducationStudentAssessmentAttemptDto attempt) =>
        Values.GetValueOrDefault(question.Id) ?? attempt.Responses.FirstOrDefault(x => x.PaperQuestionId == question.Id)?.Response ?? new() { Type = question.Type };

    public EducationAssessmentResponseValue Edit(EducationStudentAssessmentQuestionDto question, EducationStudentAssessmentAttemptDto attempt)
    {
        if (attempt.Paper.Questions.All(x => x.Id != question.Id)) throw new InvalidOperationException();
        if (Values.TryGetValue(question.Id, out var value)) return value;
        value = EducationAssessmentResponseValue.Parse(Value(question, attempt).ToCanonicalJson());
        Values.Add(question.Id, value); BaseRevisionIds.Add(question.Id, Head(question.Id, attempt)); return value;
    }

    public bool HasConflict(Guid questionId, EducationStudentAssessmentAttemptDto attempt) => Values.ContainsKey(questionId) &&
        (!BaseRevisionIds.TryGetValue(questionId, out var basis) || basis != Head(questionId, attempt));

    public void ConfirmLocal(Guid questionId, EducationStudentAssessmentAttemptDto attempt)
    {
        if (!Values.ContainsKey(questionId) || attempt.Paper.Questions.All(x => x.Id != questionId)) throw new InvalidOperationException();
        BaseRevisionIds[questionId] = Head(questionId, attempt);
    }

    public void AttachUploaded(EducationStudentAssessmentQuestionDto question, EducationStudentAssessmentAttemptDto attempt,
        EducationAssessmentAnswerImageDto image, Guid? replaces = null)
    {
        if (!image.IsUploaded || image.Id == Guid.Empty || image.AttemptId != attempt.Id || image.PaperQuestionId != question.Id ||
            question.Type != QuestionType.Subjective) throw new InvalidOperationException();
        var current = Value(question, attempt);
        if (current.Images.Any(x => x.AttachmentId == image.Id)) return;
        var index = replaces.HasValue ? current.Images.FindIndex(x => x.AttachmentId == replaces) : -1;
        if (replaces.HasValue && index < 0 || !replaces.HasValue && current.Images.Count >= EducationAssessmentResponseValue.MaxImages) throw new InvalidOperationException();
        var value = Edit(question, attempt); var entry = new EducationAssessmentImageResponse { AttachmentId = image.Id };
        if (replaces.HasValue) value.Images[index] = entry; else value.Images.Add(entry);
    }

    public void Drop(Guid questionId) { Values.Remove(questionId); BaseRevisionIds.Remove(questionId); }
    public void Clear() { Values.Clear(); BaseRevisionIds.Clear(); }
    private static Guid? Head(Guid questionId, EducationStudentAssessmentAttemptDto attempt) => attempt.Responses.FirstOrDefault(x => x.PaperQuestionId == questionId)?.Id;
}
