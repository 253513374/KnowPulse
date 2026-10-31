using System;
using Volo.Abp;

namespace KnowPulse.Education;

public static class EducationRules
{
    public static Guid RequireId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new BusinessException(EducationErrorCodes.InvalidIdentifier);
        }

        return id;
    }

    public static DateTime RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new BusinessException(EducationErrorCodes.UtcRequired);
        }

        return value;
    }

    public static void ValidatePeriod(DateTime fromUtc, DateTime? toUtc)
    {
        RequireUtc(fromUtc);
        if (toUtc.HasValue)
        {
            RequireUtc(toUtc.Value);
            if (toUtc.Value <= fromUtc)
            {
                throw new BusinessException(EducationErrorCodes.InvalidEffectivePeriod);
            }
        }
    }

    public static bool IsEffectiveAt(DateTime fromUtc, DateTime? toUtc, DateTime atUtc)
    {
        RequireUtc(atUtc);
        return fromUtc <= atUtc && (!toUtc.HasValue || atUtc < toUtc.Value);
    }

    public static bool Overlaps(DateTime firstFromUtc, DateTime? firstToUtc, DateTime secondFromUtc, DateTime? secondToUtc)
    {
        ValidatePeriod(firstFromUtc, firstToUtc);
        ValidatePeriod(secondFromUtc, secondToUtc);
        return (!firstToUtc.HasValue || secondFromUtc < firstToUtc.Value) &&
               (!secondToUtc.HasValue || firstFromUtc < secondToUtc.Value);
    }

    public static DateTime EndPeriod(DateTime fromUtc, DateTime? currentToUtc, DateTime toUtc)
    {
        ValidatePeriod(fromUtc, toUtc);
        if (currentToUtc.HasValue && toUtc > currentToUtc.Value)
        {
            throw new BusinessException(EducationErrorCodes.CannotExtendEndedRelation);
        }

        return toUtc;
    }

    public static string NormalizeCode(string code, int maximumLength)
    {
        return Check.NotNullOrWhiteSpace(code, nameof(code), maximumLength).Trim().ToUpperInvariant();
    }
}
