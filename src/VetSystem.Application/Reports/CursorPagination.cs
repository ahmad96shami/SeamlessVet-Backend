using System.Text;
using VetSystem.Domain.Common;

namespace VetSystem.Application.Reports;

/// <summary>
/// A keyset (cursor) position for feed-like report lists (M12 task 16, TECH_STACK "API Design Notes":
/// cursor pagination for feed lists such as visits, offset for admin tables). The position is the
/// <c>(CreatedAt, Id)</c> of the last row returned; the next page selects rows strictly "older" than it
/// under a <c>CreatedAt DESC, Id DESC</c> order, so paging is stable even as new rows are inserted —
/// unlike <c>OFFSET</c>, which shifts. <c>Id</c> breaks ties on identical <c>CreatedAt</c>.
/// </summary>
public readonly record struct ReportCursor(DateTimeOffset CreatedAt, Guid Id);

/// <summary>
/// Encodes / decodes the opaque cursor token carried in <c>?cursor=</c> and clamps <c>?limit=</c>. The
/// token is base64url(<c>"{utcTicks}:{id:N}"</c>) — opaque to clients, URL-safe, and self-validating
/// (a malformed token fails fast with <c>invalid_cursor</c> rather than silently returning the first
/// page). Pure (no EF / ASP.NET), so it unit-tests in isolation and is reusable by any feed list.
/// </summary>
public static class CursorPagination
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>Page size clamped to <c>[1, <see cref="MaxPageSize"/>]</c> (defaults to <see cref="DefaultPageSize"/>).</summary>
    public static int ClampLimit(int? limit) => Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);

    /// <summary>Encodes a keyset position into the opaque, URL-safe token returned as <c>nextCursor</c>.</summary>
    public static string Encode(DateTimeOffset createdAt, Guid id)
    {
        var raw = $"{createdAt.UtcTicks}:{id:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Decodes a cursor token. Returns <c>null</c> for a null/blank token (first page);
    /// throws <see cref="ConflictException"/> <c>invalid_cursor</c> for a malformed one.
    /// </summary>
    public static ReportCursor? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var s = cursor.Replace('-', '+').Replace('_', '/');
            s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(s));

            var separator = raw.IndexOf(':');
            if (separator <= 0)
            {
                throw new FormatException("missing separator");
            }

            var ticks = long.Parse(raw[..separator]);
            var id = Guid.ParseExact(raw[(separator + 1)..], "N");
            return new ReportCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new ConflictException("invalid_cursor", "The pagination cursor is malformed.");
        }
    }
}
