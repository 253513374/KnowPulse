using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using KnowPulse.Education;
using Volo.Abp;

namespace KnowPulse.KnowledgeBase;

public partial class KnowledgeItem
{
    // shortcut: bookmark history shares this row; use a dedicated collection before raising the 200-record limit.
    private const string NotebookSourceKey = "PrivateNotebookSourcesV1";
    public const int MaxNotebookSourceRecords = 200;
    public const int MaxNotebookSourcesPerVersion = 20;
    private static BusinessException InvalidNotebookSource() => new(EducationNotebookSourceErrors.Invalid);
    private static bool Fingerprint(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);

    public IReadOnlyList<EducationPrivateNotebookSource> GetPrivateNotebookSources()
    {
        if (!LibraryId.HasValue || !OwnerUserId.HasValue) throw InvalidNotebookSource();
        if (!ExtraProperties.TryGetValue(NotebookSourceKey, out var value)) return Array.Empty<EducationPrivateNotebookSource>();
        var raw = value?.ToString();
        if (string.IsNullOrEmpty(raw) || raw.Length > 262144) throw InvalidNotebookSource();
        List<EducationPrivateNotebookSource> entries;
        try { entries = JsonSerializer.Deserialize<List<EducationPrivateNotebookSource>>(raw) ?? throw InvalidNotebookSource(); }
        catch (JsonException) { throw InvalidNotebookSource(); }
        if (entries.Count > MaxNotebookSourceRecords || entries.Any(x => x == null || x.Id == Guid.Empty || x.NoteVersionId == Guid.Empty || x.Source == null ||
            !Fingerprint(x.RequestFingerprint) || x.RecordedAtUtc.Kind != DateTimeKind.Utc || x.RemovalOperationId == Guid.Empty ||
            x.RemovalOperationId.HasValue != x.RemovedAtUtc.HasValue || x.RemovalOperationId.HasValue != (x.RemovalFingerprint != null) ||
            x.RemovedAtUtc.HasValue && (x.RemovedAtUtc.Value.Kind != DateTimeKind.Utc || x.RemovedAtUtc < x.RecordedAtUtc || !Fingerprint(x.RemovalFingerprint))))
            throw InvalidNotebookSource();
        foreach (var entry in entries) entry.Source.Validate();
        var operations = entries.Select(x => x.Id).Concat(entries.Where(x => x.RemovalOperationId.HasValue).Select(x => x.RemovalOperationId!.Value)).ToList();
        if (operations.Distinct().Count() != operations.Count) throw InvalidNotebookSource();
        var active = entries.Where(x => !x.RemovedAtUtc.HasValue).ToList();
        if (active.GroupBy(x => x.NoteVersionId).Any(x => x.Count() > MaxNotebookSourcesPerVersion) ||
            active.GroupBy(x => (x.NoteVersionId, x.Source)).Any(x => x.Count() > 1)) throw InvalidNotebookSource();
        return entries;
    }

    public EducationPrivateNotebookSource? FindNotebookSourceOperation(Guid operationId, string? fingerprint = null)
    {
        if (operationId == Guid.Empty) throw InvalidNotebookSource();
        var entry = GetPrivateNotebookSources().SingleOrDefault(x => x.Id == operationId || x.RemovalOperationId == operationId);
        if (entry != null && fingerprint != null && (entry.Id == operationId ? entry.RequestFingerprint : entry.RemovalFingerprint) != fingerprint)
            throw new BusinessException(EducationNotebookSourceErrors.Conflict);
        return entry;
    }

    public EducationPrivateNotebookSource AddNotebookSource(Guid operationId, Guid noteVersionId, EducationNotebookSourcePointer source, string fingerprint, DateTime now)
    {
        if (operationId == Guid.Empty || noteVersionId == Guid.Empty || source == null || !Fingerprint(fingerprint) || now.Kind != DateTimeKind.Utc) throw InvalidNotebookSource();
        source.Validate(); var prior = FindNotebookSourceOperation(operationId, fingerprint); if (prior != null) return prior;
        var entries = GetPrivateNotebookSources().ToList();
        if (entries.Count >= MaxNotebookSourceRecords || entries.Count(x => x.NoteVersionId == noteVersionId && !x.RemovedAtUtc.HasValue) >= MaxNotebookSourcesPerVersion)
            throw new BusinessException(EducationNotebookSourceErrors.Limit);
        if (entries.Any(x => x.NoteVersionId == noteVersionId && x.Source == source && !x.RemovedAtUtc.HasValue))
            throw new BusinessException(EducationNotebookSourceErrors.Conflict);
        var entry = new EducationPrivateNotebookSource { Id = operationId, NoteVersionId = noteVersionId, Source = source, RequestFingerprint = fingerprint, RecordedAtUtc = now };
        entries.Add(entry); ExtraProperties[NotebookSourceKey] = JsonSerializer.Serialize(entries); return entry;
    }

    public EducationPrivateNotebookSource RemoveNotebookSource(Guid operationId, Guid sourceId, string fingerprint, DateTime now)
    {
        if (operationId == Guid.Empty || sourceId == Guid.Empty || !Fingerprint(fingerprint) || now.Kind != DateTimeKind.Utc) throw InvalidNotebookSource();
        var prior = FindNotebookSourceOperation(operationId, fingerprint); if (prior != null) return prior;
        var entries = GetPrivateNotebookSources().ToList(); var index = entries.FindIndex(x => x.Id == sourceId);
        if (index < 0 || entries[index].RemovedAtUtc.HasValue || now < entries[index].RecordedAtUtc) throw InvalidNotebookSource();
        var removed = entries[index] with { RemovalOperationId = operationId, RemovalFingerprint = fingerprint, RemovedAtUtc = now };
        entries[index] = removed; ExtraProperties[NotebookSourceKey] = JsonSerializer.Serialize(entries); return removed;
    }
}
