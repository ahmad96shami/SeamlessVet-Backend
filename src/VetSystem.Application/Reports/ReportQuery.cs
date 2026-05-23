using VetSystem.Domain.Common;

namespace VetSystem.Application.Reports;

/// <summary>
/// Shared primitives for the read-only report services (M12, PRD §7.9). Every report is an
/// environment-scoped, offset-paged admin table (TECH_STACK "API Design Notes") filtered by a
/// <c>[from, to]</c> day period. This helper turns that period into a half-open UTC instant window
/// and clamps paging the same way across all reports, so the convention lives in exactly one place.
///
/// <para>Report timestamps (<c>issued_at</c>, <c>created_at</c>, …) are <c>timestamptz</c>, so the
/// window is compared in UTC. The bounds default to the full range of time when a side is unset,
/// which lets the report queries apply an unconditional <c>&gt;= Start &amp;&amp; &lt; End</c> predicate
/// that EF/Npgsql parameterises cleanly.</para>
/// </summary>
public static class ReportQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>
    /// Resolves an inclusive <c>[from, to]</c> day range to a half-open UTC instant window
    /// <c>[Start, End)</c>: <c>Start</c> is 00:00 on <paramref name="from"/> (or the beginning of time
    /// when unset); <c>End</c> is 00:00 on the day <em>after</em> <paramref name="to"/> (or the end of
    /// time when unset). A row stamped at 23:59 on the <c>to</c> day therefore falls inside the window.
    /// </summary>
    /// <exception cref="ConflictException">
    /// Code <c>invalid_period</c> when both ends are supplied and <paramref name="from"/> &gt; <paramref name="to"/>.
    /// </exception>
    public static (DateTimeOffset Start, DateTimeOffset End) ResolveWindow(DateOnly? from, DateOnly? to)
    {
        if (from is { } f && to is { } t && f > t)
        {
            throw new ConflictException(
                "invalid_period",
                $"The period start {f:yyyy-MM-dd} is after the end {t:yyyy-MM-dd}.");
        }

        var start = from is { } ff
            ? new DateTimeOffset(ff.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        var end = to is { } tt
            ? new DateTimeOffset(tt.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : DateTimeOffset.MaxValue;

        return (start, end);
    }

    /// <summary>Non-negative row offset (defaults to 0).</summary>
    public static int ClampSkip(int? skip) => Math.Max(0, skip ?? 0);

    /// <summary>Page size clamped to <c>[1, <see cref="MaxPageSize"/>]</c> (defaults to <see cref="DefaultPageSize"/>).</summary>
    public static int ClampTake(int? take) => Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize);
}
