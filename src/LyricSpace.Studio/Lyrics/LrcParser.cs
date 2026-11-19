using System.Globalization;
using System.Text.RegularExpressions;

namespace LyricSpace.Studio.Lyrics;

internal readonly record struct LyricCue(double Seconds, string Text, TimedWord[]? Words = null);

// Timestamp parsing adapted from LyricsOnTheGo (MIT). See THIRD_PARTY_NOTICES.md.
internal static partial class LrcParser
{
    internal const int MaxCharacters = 128 * 1024;

    [GeneratedRegex(@"\[([0-9]{1,4}):([0-5]?[0-9])(?:[.:]([0-9]{1,3}))?\]")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"(?im)^\s*\[offset:\s*([+-]?[0-9]{1,9})\s*\]")]
    private static partial Regex Offset();

    [GeneratedRegex(@"<([0-9]{1,4}):([0-5]?[0-9])(?:[.:]([0-9]{1,3}))?>")]
    private static partial Regex InlineTimestamp();

    public static LyricCue[] Parse(string lrc)
    {
        if (lrc.Length > MaxCharacters)
        {
            return [];
        }

        Match offsetTag = Offset().Match(lrc.TrimStart('\uFEFF'));
        double offset = offsetTag.Success
            ? int.Parse(offsetTag.Groups[1].ValueSpan, CultureInfo.InvariantCulture) / 1000.0 : 0;
        var lines = new SortedDictionary<double, LyricCue>();
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

            string body = line[end..].Trim();
            string text = InlineTimestamp().Replace(body, "").Trim();
            Match[] tags = Timestamp().Matches(line[..end]).ToArray();
            double first = Time(tags[0]);
            foreach (Match tag in tags)
            {
                double stamp = Time(tag);
                double start = Math.Max(0, stamp - offset);
                var words = ParseWords(body, start, stamp - first - offset);
                var cue = new LyricCue(start, text, words);
                // Preserve explicit silence and handle multiple variants at a single timestamp.
                if (start == 0 || !lines.TryGetValue(start, out LyricCue previous) || previous.Text.Length == 0)
                {
                    lines[start] = cue;
                }
                else if (text.Length != 0 && previous.Text != text)
                {
                    lines[start] = new LyricCue(start, previous.Text + " / " + text);
                }
                else if (previous.Words is null && words is not null)
                {
                    lines[start] = cue;
                }
            }
        }

        return lines.Values.ToArray();
    }

    private static TimedWord[]? ParseWords(string body, double start, double shift)
    {
        Match[] tags = InlineTimestamp().Matches(body).ToArray();
        if (tags.Length == 0 || tags[0].Index != 0)
        {
            return null;
        }

        var words = new List<TimedWord>();
        for (int i = 0; i < tags.Length; i++)
        {
            Match tag = tags[i];
            int finish = i + 1 < tags.Length ? tags[i + 1].Index : body.Length;
            string content = body[(tag.Index + tag.Length)..finish].Trim();
            if (content.Length == 0)
            {
                continue;
            }

            double time = Math.Max(start, Time(tag) + shift);
            double? end = i + 1 < tags.Length ? Time(tags[i + 1]) + shift : null;
            if ((words.Count != 0 && time <= words[^1].Start) || end <= time)
            {
                return null;
            }

            words.Add(new TimedWord(content, time, end));
        }

        return words.Count > 0 ? words.ToArray() : null;
    }

    private static double Time(Match tag)
    {
        int minutes = int.Parse(tag.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        int seconds = int.Parse(tag.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
        Group fraction = tag.Groups[3];
        double digits = fraction.Success
            ? int.Parse(fraction.ValueSpan, CultureInfo.InvariantCulture) / Math.Pow(10, fraction.Length)
            : 0;
        return minutes * 60 + seconds + digits;
    }
}
