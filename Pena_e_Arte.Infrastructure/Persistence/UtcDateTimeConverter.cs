using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Pena_e_Arte.Infrastructure.Persistence;

/// <summary>
/// All persisted timestamps are UTC by convention. MySQL DATETIME carries no kind,
/// so values read back arrive as Kind=Unspecified and would be serialized without
/// the trailing 'Z' — browsers then mis-parse them as local time. These converters
/// stamp DateTimeKind.Utc on read (and normalise on write) so every API response
/// consistently carries UTC ISO-8601 values.
/// </summary>
public class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public class UtcNullableDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v == null
        ? v
        : v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
    v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
