using System.Globalization;

namespace SwiftBase.Models;

public static class ValueFormatter
{
    /// <summary>Short, display-safe representation (large blobs are elided).</summary>
    public static string? Format(object? value) => Format(value, truncateBlobs: true);

    /// <summary>Lossless representation for CSV/JSON/INSERT exports.</summary>
    public static string? FormatExport(object? value) => Format(value, truncateBlobs: false);

    private static string? Format(object? value, bool truncateBlobs)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        return value switch
        {
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeOnly timeOnly => timeOnly.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TimeSpan timeSpan => timeSpan.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            byte[] bytes when truncateBlobs && bytes.Length > 24 =>
                $"0x{Convert.ToHexString(bytes.AsSpan(0, 24))}…",
            byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }
}
