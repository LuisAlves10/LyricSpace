using System.Globalization;

namespace LyricSpace.Studio.Lyrics;

internal enum TimingKind { Line, Authored, Estimated }
internal readonly record struct TimedWord(string Text, double Start, double? End = null);
internal readonly record struct VocalWord(string Text, double Start, double End, TimingKind Timing);

internal static class WordTiming
{
    public static VocalWord[] Resolve(LyricCue cue, double phraseEnd, bool estimate)
    {
        if (cue.Words is { Length: > 0 } words && words.All(word => word.Start < phraseEnd))
        {
            return words.Select((word, i) =>
            {
                double next = i + 1 < words.Length ? words[i + 1].Start : phraseEnd;
                return new VocalWord(word.Text, word.Start, Math.Min(word.End ?? next, next), TimingKind.Authored);
            }).ToArray();
        }

        string[] tokens = cue.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (!estimate || tokens.Length == 0)
        {
            return tokens.Select(text => new VocalWord(text, cue.Seconds, phraseEnd, TimingKind.Line)).ToArray();
        }

        double[] weights = tokens.Select(text => Math.Sqrt(new StringInfo(text).LengthInTextElements)
            + (char.IsPunctuation(text[^1]) ? 0.36 : 0)).ToArray();
        double budget = phraseEnd - cue.Seconds;
        double total = weights.Sum();
        double time = cue.Seconds;
        var estimated = new VocalWord[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            double end = i + 1 == tokens.Length ? phraseEnd : time + budget * weights[i] / total;
            estimated[i] = new VocalWord(tokens[i], time, end, TimingKind.Estimated);
            time = end;
        }
        return estimated;
    }
}
