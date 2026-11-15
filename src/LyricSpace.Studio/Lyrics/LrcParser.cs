using System.Globalization;
using System.Text.RegularExpressions;

namespace LyricSpace.Studio.Lyrics;

internal readonly record struct LyricCue(double Seconds, string Text);

// Timestamp parsing adapted from LyricsOnTheGo (MIT). See THIRD_PARTY_NOTICES.md.
internal static partial class LrcParser
{
    internal const int MaxCharacters = 128 * 1024;

    [GeneratedRegex(@"\[([0-9]{1,4}):([0-5]?[0-9])(?:[.:]([0-9]{1,3}))?\]")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"(?im)^\s*\[offset:\s*([+-]?[0-9]{1,9})\s*\]")]
    private static partial Regex Offset();

    public static LyricCue[] Parse(string lrc)
    {
        if (lrc.Length > MaxCharacters)
        {
            return [];
        }

        Match offsetTag = Offset().Match(lrc.TrimStart('\uFEFF'));
        double offset = offsetTag.Success
            ? int.Parse(offsetTag.Groups[1].ValueSpan, CultureInfo.InvariantCulture) / 1000.0 : 0;
        var lines = new SortedDictionary<double, string>();
        using var reader = new StringReader(lrc);
        while (reader.ReadLine() is { } raw)
        {
            string line = raw.TrimStart('\uFEFF', ' ', '\t');
            int end = 0;
            while (Timestamp().Match(line, end) is { Success: true } tag && tag.Index == end)
            {
                end += tag.Length;
            }

            if (end == 0)
            {
                continue;
            }

            string text = line[end..].Trim();
            foreach (Match tag in Timestamp().Matches(line[..end]))
            {
                int minutes = int.Parse(tag.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
                int seconds = int.Parse(tag.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
                Group fraction = tag.Groups[3];
                double digits = fraction.Success
                    ? int.Parse(fraction.ValueSpan, CultureInfo.InvariantCulture) / Math.Pow(10, fraction.Length)
                    : 0;
                double start = Math.Max(0, minutes * 60 + seconds + digits - offset);
                // Keep the latest negative cue at zero and preserve explicit blank markers.
                if (start == 0 || !lines.TryGetValue(start, out string? previous) || previous.Length == 0)
                {
                    lines[start] = text;
                }
                else if (text.Length != 0 && previous != text)
                {
                    lines[start] = previous + " / " + text;
                }
            }
        }

        return lines.Select(line => new LyricCue(line.Key, line.Value)).ToArray();
    }
}
