using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationSchoolAppServiceTests
{
    private static SubmitTeachingRelationRequestDto RequestRelation(Guid? original = null) => new() { OperationId = Guid.NewGuid(), OriginalAssignmentId = original, Text = "请核对本人任教班级和生效时间", ConfirmSharingWithSchool = true };
    private static ResolveTeachingRelationRequestDto RelationDecision(TeachingRelationRequestDto row, TeachingRelationResolution decision) => new() {
        OperationId = Guid.NewGuid(), ConcurrencyStamp = row.ConcurrencyStamp, SchoolConfigurationRevision = row.SchoolConfigurationRevision,
        Resolution = decision, OriginalAssignmentStamp = row.OriginalAssignmentStamp, EffectiveFromUtc = decision == TeachingRelationResolution.Reject ? null : DateTime.UtcNow,
        Text = "学校已核对确切账号与关系，保存本次处理说明", ConfirmReviewedAccountAndChange = true };

    [Fact]
    public async Task Teaching_Relation_Requests_Should_Change_Only_The_Requester_And_Preserve_Original_Receipts_And_History()
    {
        var f = await CreateFixtureAsync(); TeachingAssignmentDto old; SchoolClassDto nextClass;
        using (ChangeUser(f.Manager)) {
            old = await _service.AssignTeacherAsync(f.School.Id, Assignment(f, f.Teacher));
            nextClass = await _service.CreateClassAsync(f.School.Id, new() { AcademicTermId = f.Term.Id, Code = "C2", Name = "七年级二班", Grade = 7 });
        }
        TeachingRelationRequestDto request; var input = RequestRelation(old.Id);
        using (ChangeUser(f.Teacher)) {
            request = await _service.SubmitRelationRequestAsync(f.School.Id, input); request.Status.ShouldBe(TeachingRelationRequestStatus.Pending);
            (await _service.SubmitRelationRequestAsync(f.School.Id, input)).Id.ShouldBe(request.Id);
            (await _service.GetMyTeachingRelationsAsync(f.School.Id, new())).Items.Single().Id.ShouldBe(old.Id);
        }
        using (ChangeUser(f.SecondTeacher)) { await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetMyRelationRequestAsync(request.Id)); }
        using (ChangeUser(f.Student)) { await Should.ThrowAsync<AbpAuthorizationException>(() => _service.SubmitRelationRequestAsync(f.School.Id, RequestRelation())); }
        Guid resultId;
        using (ChangeUser(f.Manager)) {
            var review = await _service.GetRelationRequestAsync(f.School.Id, request.Id); var decision = RelationDecision(review, TeachingRelationResolution.Replace);
            decision.AcademicTermId = f.Term.Id; decision.SchoolClassId = nextClass.Id; decision.EducationSubjectId = f.Subject.Id;
            var result = await _service.ResolveRelationRequestAsync(f.School.Id, request.Id, decision); resultId = result.ResultingAssignmentId!.Value;
            result.Status.ShouldBe(TeachingRelationRequestStatus.Processed); result.CompletionOperationId.ShouldBe(decision.OperationId);
            result.OriginalEndsAtRequestUtc.ShouldBe(f.Term.EndsAtUtc); result.Resulting!.EffectiveFromUtc.ShouldBe(result.EndedAtUtc!.Value);
            (await _service.ResolveRelationRequestAsync(f.School.Id, request.Id, decision)).ResultingAssignmentId.ShouldBe(resultId);
        }
        using (ChangeUser(f.Teacher)) {
            var result = await _service.GetMyRelationRequestAsync(request.Id); result.ResultingAssignmentId.ShouldBe(resultId); result.Original!.EffectiveToUtc.ShouldBe(result.EndedAtUtc);
            (await _service.GetMyTeachingRelationsAsync(f.School.Id, new())).TotalCount.ShouldBe(2);
            var workspace = (await _service.GetMyWorkspaceAsync()).Workspaces.Single(x => x.SchoolId == f.School.Id && x.Role == SchoolMemberRole.Teacher);
            workspace.Classes.Single().RelationId.ShouldBe(resultId);
        }
        await WithUnitOfWorkAsync(async () => {
            var all = await _assignments.GetListAsync(x => x.SchoolId == f.School.Id); all.Count.ShouldBe(2); all.ShouldAllBe(x => x.TeacherUserId == f.Teacher);
            (await _assignments.GetAsync(old.Id)).SchoolClassId.ShouldBe(f.Class.Id);
        });
    }

    [Fact]
    public async Task Teaching_Relation_Requests_Should_Reject_Stale_Or_Invalid_Changes_Before_Ending_A_Relation_And_Keep_Withdrawals_Separate()
    {
        var f = await CreateFixtureAsync(); TeachingAssignmentDto old;
        using (ChangeUser(f.Manager)) { old = await _service.AssignTeacherAsync(f.School.Id, Assignment(f, f.Teacher)); }
        using (ChangeUser(f.SecondTeacher)) { await Should.ThrowAsync<AbpAuthorizationException>(() => _service.SubmitRelationRequestAsync(f.School.Id, RequestRelation(old.Id))); }
        TeachingRelationRequestDto request;
        using (ChangeUser(f.Teacher)) {
            var withdrawn = await _service.SubmitRelationRequestAsync(f.School.Id, RequestRelation());
            var command = new WithdrawTeachingRelationRequestDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = withdrawn.ConcurrencyStamp };
            (await _service.WithdrawMyRelationRequestAsync(withdrawn.Id, command)).Status.ShouldBe(TeachingRelationRequestStatus.Withdrawn);
            (await _service.WithdrawMyRelationRequestAsync(withdrawn.Id, command)).CompletionOperationId.ShouldBe(command.OperationId);
            request = await _service.SubmitRelationRequestAsync(f.School.Id, RequestRelation(old.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.ResolveRelationRequestAsync(f.School.Id, request.Id, RelationDecision(request, TeachingRelationResolution.Reject)));
        }
        using (ChangeUser(f.Manager)) {
            var review = await _service.GetRelationRequestAsync(f.School.Id, request.Id); var invalid = RelationDecision(review, TeachingRelationResolution.Replace);
            invalid.AcademicTermId = f.Term.Id; invalid.SchoolClassId = Guid.NewGuid(); invalid.EducationSubjectId = f.Subject.Id;
            await Should.ThrowAsync<EntityNotFoundException>(() => _service.ResolveRelationRequestAsync(f.School.Id, request.Id, invalid));
            (await WithUnitOfWorkAsync(() => _assignments.GetAsync(old.Id))).EffectiveToUtc.ShouldBe(f.Term.EndsAtUtc);
            var end = RelationDecision(await _service.GetRelationRequestAsync(f.School.Id, request.Id), TeachingRelationResolution.End);
            (await _service.ResolveRelationRequestAsync(f.School.Id, request.Id, end)).EndedAtUtc.ShouldNotBeNull();
        }
        using (ChangeUser(f.Teacher)) { request = await _service.SubmitRelationRequestAsync(f.School.Id, RequestRelation()); }
        TeachingRelationRequestDto added;
        using (ChangeUser(f.Manager)) {
            var add = RelationDecision(await _service.GetRelationRequestAsync(f.School.Id, request.Id), TeachingRelationResolution.Add); add.AcademicTermId = f.Term.Id; add.SchoolClassId = f.Class.Id; add.EducationSubjectId = f.Subject.Id;
            added = await _service.ResolveRelationRequestAsync(f.School.Id, request.Id, add);
        }
        using (ChangeUser(f.Teacher)) { request = await _service.SubmitRelationRequestAsync(f.School.Id, RequestRelation(added.ResultingAssignmentId)); }
        using (ChangeUser(f.Manager)) {
            await _service.EndTeachingAssignmentAsync(f.School.Id, added.ResultingAssignmentId!.Value, new() { EffectiveToUtc = DateTime.UtcNow });
            var review = await _service.GetRelationRequestAsync(f.School.Id, request.Id); review.OriginalChanged.ShouldBeTrue();
            (await Should.ThrowAsync<BusinessException>(() => _service.ResolveRelationRequestAsync(f.School.Id, request.Id, RelationDecision(review, TeachingRelationResolution.End)))).Code.ShouldBe(TeachingRelationRequestErrors.Changed);
            var rejected = await _service.ResolveRelationRequestAsync(f.School.Id, request.Id, RelationDecision(review, TeachingRelationResolution.Reject)); rejected.Resolution.ShouldBe(TeachingRelationResolution.Reject);
            var member = (await _service.GetMembershipsAsync(f.School.Id, new() { UserId = f.Teacher, Role = SchoolMemberRole.Teacher })).Items.Single();
            await _service.EndMembershipAsync(f.School.Id, member.Id, new() { EffectiveToUtc = DateTime.UtcNow });
        }
        using (ChangeUser(f.Teacher)) { (await _service.GetMyRelationRequestsAsync(f.School.Id, new())).TotalCount.ShouldBe(0); await Should.ThrowAsync<AbpAuthorizationException>(() => _service.GetMyRelationRequestAsync(request.Id)); }
    }
}
