using System.Data;
using Dapper;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Global Dapper type handlers — fix DTO materialization mismatches that previously required
/// per-handler intermediate "Row" records (see feedback memory `feedback-dapper-datetimeoffset-materialization`).
///
/// SQL Server `datetime2` columns return <see cref="DateTime"/> via SqlClient. Many DTOs (records
/// in particular) declare <see cref="DateTimeOffset"/> properties. Without a type handler, Dapper
/// fails materialization for records ("no matching constructor signature") and for classes throws
/// <see cref="InvalidCastException"/> on setter.
///
/// Register ONCE at app startup. Idempotent — Dapper replaces existing handler for same type.
/// </summary>
public static class DapperTypeHandlers
{
    private static int _registered;

    /// <summary>Register all custom Dapper type handlers. Safe to call multiple times.</summary>
    public static void Register()
    {
        // Idempotency guard for parallel app starts (tests, host integration fixtures).
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeOffsetTypeHandler());
    }

    /// <summary>
    /// Maps SQL <c>datetime2</c> ↔ .NET <see cref="DateTimeOffset"/>.
    /// Read: stamps <see cref="TimeSpan.Zero"/> (UTC) on the resulting offset — datetime2 is
    /// timezone-naive so we treat persisted values as UTC by convention. If different policy
    /// needed (eg. local), update here in one place.
    /// </summary>
    private sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero),
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s),
            _ => throw new InvalidCastException(
                $"Cannot convert {value?.GetType().FullName ?? "null"} to DateTimeOffset")
        };

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.DateTime2;
            parameter.Value = value.UtcDateTime;
        }
    }

    /// <summary>Nullable variant — preserves NULL passthrough on both directions.</summary>
    private sealed class NullableDateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset?>
    {
        public override DateTimeOffset? Parse(object value) => value switch
        {
            null => null,
            DBNull => null,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero),
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s),
            _ => throw new InvalidCastException(
                $"Cannot convert {value.GetType().FullName} to DateTimeOffset?")
        };

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset? value)
        {
            parameter.DbType = DbType.DateTime2;
            parameter.Value = value.HasValue ? value.Value.UtcDateTime : DBNull.Value;
        }
    }
}
