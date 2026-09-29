using System.Globalization;

namespace StorageExplorer.Web.Tables;

/// <summary>
/// Turns the request's flat, string-typed properties into the .NET values <see cref="Azure.Data.Tables.TableEntity"/>
/// needs to pick the right EDM type: a <see cref="long"/> (or <see cref="double"/> when it does not fit one) for
/// "Number", <see cref="bool"/> for "Boolean", <see cref="DateTimeOffset"/> for "DateTime", <see cref="Guid"/> for
/// "Guid", otherwise the raw string. Kept at the HTTP boundary (the endpoint, not the service) because it is about the
/// request's shape, not about talking to the table: see <see cref="TableEndpoints"/>.
/// </summary>
internal static class EntityPropertyParser
{
    // PartitionKey/RowKey are already their own request fields, Timestamp and odata.etag are server-assigned; letting
    // any of them through a property would silently collide with (or be dropped alongside) those.
    private static readonly string[] ReservedNames = ["PartitionKey", "RowKey", "Timestamp", "odata.etag"];

    public static bool TryParse(
        IReadOnlyList<EntityPropertyInput> properties, out IReadOnlyDictionary<string, object?> parsed, out string? error)
    {
        var result = new Dictionary<string, object?>(properties.Count);

        foreach (var property in properties)
        {
            var name = property.Name?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                parsed = result;
                error = "Every property needs a name.";
                return false;
            }

            if (Array.IndexOf(ReservedNames, name) >= 0)
            {
                parsed = result;
                error = $"\"{name}\" is set automatically and cannot be used as a property name.";
                return false;
            }

            if (!TryParseValue(property.Type, property.Value, out var value, out var valueError))
            {
                parsed = result;
                error = $"\"{name}\": {valueError}";
                return false;
            }

            result[name] = value;
        }

        parsed = result;
        error = null;
        return true;
    }

    private static bool TryParseValue(string type, string? value, out object? parsed, out string? error)
    {
        switch (type)
        {
            case "String":
                parsed = value ?? "";
                error = null;
                return true;

            case "Number":
                return TryParseNumber(value, out parsed, out error);

            case "Boolean":
                if (!bool.TryParse(value, out var boolean))
                {
                    parsed = null;
                    error = "Choose true or false.";
                    return false;
                }
                parsed = boolean;
                error = null;
                return true;

            case "DateTime":
                if (!DateTimeOffset.TryParse(
                        value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
                {
                    parsed = null;
                    error = "Enter a valid date and time.";
                    return false;
                }
                parsed = date;
                error = null;
                return true;

            case "Guid":
                if (!Guid.TryParse(value, out var guid))
                {
                    parsed = null;
                    error = $"\"{value}\" is not a GUID.";
                    return false;
                }
                parsed = guid;
                error = null;
                return true;

            default:
                parsed = null;
                error = $"Unknown property type \"{type}\".";
                return false;
        }
    }

    // "Number" is one bucket in the UI, not a choice between Int32/Int64/Double: a whole number becomes an Int64 (the
    // wider of the two integer EDM types, so it round-trips more of what a user might type), anything else a Double.
    private static bool TryParseNumber(string? value, out object? parsed, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = null;
            error = "Enter a number.";
            return false;
        }

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            parsed = integer;
            error = null;
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
        {
            parsed = real;
            error = null;
            return true;
        }

        parsed = null;
        error = $"\"{value}\" is not a number.";
        return false;
    }
}
