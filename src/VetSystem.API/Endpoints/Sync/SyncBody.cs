using System.Text.Json;
using VetSystem.Domain.Common;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// Helpers for reading sync payload <see cref="JsonElement"/>s. PowerSync ships snake_case
/// columns; the handlers stay terse by going through this rather than rolling per-field guards.
/// </summary>
internal static class SyncBody
{
    public static string RequireString(JsonElement body, string field)
    {
        if (!body.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw new ConflictException("invalid_payload", $"'{field}' is required and must be a string.");
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ConflictException("invalid_payload", $"'{field}' must not be empty.");
        }

        return value;
    }

    public static string RequireString(JsonElement body, string field, IReadOnlyCollection<string> allowed, string entity)
    {
        var value = RequireString(body, field);
        if (!allowed.Contains(value))
        {
            throw new ConflictException("invalid_enum_value",
                $"{entity}.{field} '{value}' is not one of: {string.Join(", ", allowed)}.");
        }

        return value;
    }

    public static string? OptionalString(JsonElement body, string field)
    {
        if (!body.TryGetProperty(field, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw new ConflictException("invalid_payload", $"'{field}' must be a string or null."),
        };
    }

    public static bool TryGetString(JsonElement body, string field, out string? value)
    {
        if (!body.TryGetProperty(field, out var element))
        {
            value = null;
            return false;
        }

        value = element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw new ConflictException("invalid_payload", $"'{field}' must be a string or null."),
        };
        return true;
    }

    public static Guid? OptionalGuid(JsonElement body, string field)
    {
        if (!body.TryGetProperty(field, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String when element.TryGetGuid(out var g) => g,
            _ => throw new ConflictException("invalid_payload", $"'{field}' must be a Guid string or null."),
        };
    }

    public static Guid RequireGuid(JsonElement body, string field)
    {
        if (!body.TryGetProperty(field, out var element) || !element.TryGetGuid(out var g))
        {
            throw new ConflictException("invalid_payload", $"'{field}' is required and must be a Guid.");
        }

        return g;
    }

    public static decimal RequireDecimal(JsonElement body, string field)
    {
        if (!body.TryGetProperty(field, out var element) || !element.TryGetDecimal(out var d))
        {
            throw new ConflictException("invalid_payload", $"'{field}' is required and must be a number.");
        }

        return d;
    }

    public static decimal? OptionalDecimal(JsonElement body, string field)
    {
        if (!body.TryGetProperty(field, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when element.TryGetDecimal(out var d) => d,
            _ => throw new ConflictException("invalid_payload", $"'{field}' must be a number or null."),
        };
    }

    public static DateOnly? OptionalDate(JsonElement body, string field)
    {
        if (!body.TryGetProperty(field, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String when DateOnly.TryParse(element.GetString(), out var d) => d,
            _ => throw new ConflictException("invalid_payload", $"'{field}' must be a date (YYYY-MM-DD) or null."),
        };
    }

    public static int? OptionalInt(JsonElement body, string field)
    {
        if (!body.TryGetProperty(field, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when element.TryGetInt32(out var i) => i,
            _ => throw new ConflictException("invalid_payload", $"'{field}' must be an integer or null."),
        };
    }

    public static bool? OptionalBool(JsonElement body, string field)
    {
        if (!body.TryGetProperty(field, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ConflictException("invalid_payload", $"'{field}' must be a boolean or null."),
        };
    }

    public static DateTimeOffset? OptionalDateTime(JsonElement body, string field)
    {
        if (!body.TryGetProperty(field, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String when DateTimeOffset.TryParse(
                element.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt) => dt,
            _ => throw new ConflictException("invalid_payload", $"'{field}' must be an ISO-8601 timestamp or null."),
        };
    }

    public static string RequireString(JsonElement body, string field, IReadOnlyCollection<string> allowed)
        => RequireString(body, field, allowed, field);
}
