using System.Globalization;

namespace PCue.Services;

public static class TimeFormat
{
    /// <summary>"1:23", "1:23.4", "1:02:03" — tenths only when non-zero.</summary>
    public static string FormatCue(long ms)
    {
        if (ms < 0)
            ms = 0;

        var tenths = (ms + 50) / 100;
        var totalSeconds = tenths / 10;
        var fraction = tenths % 10;
        var h = totalSeconds / 3600;
        var m = totalSeconds / 60 % 60;
        var s = totalSeconds % 60;

        var core = h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        return fraction == 0 ? core : $"{core}.{fraction}";
    }

    public static string FormatSeconds(int ms)
        => (ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Rounds up so the display never sits on 00:00 before firing.</summary>
    public static string FormatCountdown(TimeSpan remaining)
    {
        var total = (long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        var h = total / 3600;
        var m = total / 60 % 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    public static string FormatClock(DateTime at)
    {
        var time = at.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var days = (at.Date - DateTime.Today).Days;
        return days switch
        {
            0 => time,
            1 => $"내일 {time}",
            _ => $"{at.ToString("M/d", CultureInfo.InvariantCulture)} {time}"
        };
    }

    /// <summary>
    /// Parses "90", "1:30", "1:30.5", "1:02:03" (comma accepted as decimal point).
    /// A bare number is multiplied by <paramref name="bareUnitMs"/>.
    /// </summary>
    public static bool TryParseDuration(string? text, long bareUnitMs, out long ms)
    {
        ms = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3)
            return false;

        var values = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            var isLast = i == parts.Length - 1;
            if (part.Length == 0 || (!isLast && part.Contains('.')))
                return false;

            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                || value < 0)
                return false;

            if (i > 0 && value >= 60)
                return false;

            values[i] = value;
        }

        double totalMs;
        if (parts.Length == 1)
        {
            totalMs = values[0] * bareUnitMs;
        }
        else
        {
            double seconds = 0;
            foreach (var v in values)
                seconds = seconds * 60 + v;
            totalMs = seconds * 1000;
        }

        if (totalMs > TimeSpan.FromDays(1).TotalMilliseconds)
            return false;

        ms = (long)Math.Round(totalMs);
        return true;
    }

    /// <summary>Seconds with optional decimals ("1.5", "1,5").</summary>
    public static bool TryParseSeconds(string? text, out int ms)
    {
        ms = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim().Replace(',', '.');
        if (normalized.EndsWith('초'))
            normalized = normalized[..^1].TrimEnd();

        if (!double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            || seconds < 0 || seconds > 3600)
            return false;

        ms = (int)Math.Round(seconds * 1000);
        return true;
    }

    /// <summary>24h time of day: "19:30", "19:30:15", "1930", "19.30", "9".</summary>
    public static bool TryParseClock(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim().Replace('.', ':');
        int h, m = 0, sec = 0;

        if (!s.Contains(':'))
        {
            if (!s.All(char.IsAsciiDigit))
                return false;

            switch (s.Length)
            {
                case 1 or 2:
                    h = int.Parse(s, CultureInfo.InvariantCulture);
                    break;
                case 3 or 4:
                    h = int.Parse(s[..^2], CultureInfo.InvariantCulture);
                    m = int.Parse(s[^2..], CultureInfo.InvariantCulture);
                    break;
                default:
                    return false;
            }
        }
        else
        {
            var parts = s.Split(':');
            if (parts.Length is < 2 or > 3
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out h)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out m)
                || (parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out sec)))
                return false;
        }

        if (h is < 0 or > 23 || m is < 0 or > 59 || sec is < 0 or > 59)
            return false;

        time = new TimeSpan(h, m, sec);
        return true;
    }
}
