using System;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Exam;

public interface IExamAttemptRepository : IRepository<ExamAttempt, Guid>
{
    Task<ExamAttempt> GetForUpdateAsync(Guid id);
}
