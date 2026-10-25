using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using KnowPulse.Exam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;

namespace KnowPulse.EntityFrameworkCore.Exam;

public class EfCoreExamAttemptRepository(IDbContextProvider<KnowPulseDbContext> provider)
    : EfCoreRepository<KnowPulseDbContext, ExamAttempt, Guid>(provider), IExamAttemptRepository
{
    public virtual async Task<ExamAttempt> GetForUpdateAsync(Guid id)
    {
        var db = await GetDbContextAsync();
        if (!db.Database.IsNpgsql()) return await GetAsync(id);
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("作答变更必须在数据库事务中执行。");
        var entity = db.Model.FindEntityType(typeof(ExamAttempt))!;
        var table = db.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        // 同一作答的保存、交卷和评分串行读取最新状态，重复交卷可返回已提交结果。
        var sql = FormattableStringFactory.Create($"SELECT * FROM {table} WHERE \"Id\" = {{0}} FOR UPDATE", id);
        return await (await GetDbSetAsync()).FromSql(sql).SingleOrDefaultAsync()
            ?? throw new EntityNotFoundException(typeof(ExamAttempt), id);
    }
}
