using System.Numerics;
using LyricSpace.Studio.Composition;

namespace LyricSpace.Studio.Rendering;

internal sealed partial class StageRenderer
{
    private readonly Dictionary<PhraseStyle, PhraseFit> _fits = new();
    private (Vector2 Half, float Feather) _fitRegion;
    private Sequence? _upcoming;
    private Queue<TextStyle>? _warmQueue;

    // Amortize the next block's raster work across frames of the current one.
    public void WarmUpcoming(Sequence? next)
    {
        if (!ReferenceEquals(next, _upcoming))
        {
            _upcoming = next;
            _warmQueue = next is null ? null : new Queue<TextStyle>(next.Planes
                .Where(plane => plane.Word is not null).Select(plane => plane.Text!.Value).Distinct());
        }

        while (_warmQueue?.TryDequeue(out TextStyle style) == true)
        {
            if (_wordMasks.Warm(style))
            {
                GlyphBytes = _masks.Bytes + _wordMasks.Bytes;
                break;
            }
        }
    }

    private PhraseDrawing BuildPhraseDrawing(PhraseStyle phrase, int[] words)
    {
        var nodes = new SortedSet<int>();
        void Include(int index)
        {
            for (;;)
            {
                if (!nodes.Add(index) || _sequence.Rig.GetNode(index).ParentIndex is not int parent)
                {
                    break;
                }
                index = parent;
            }
        }

        Include(_sequence.CameraNode);
        foreach (int word in words)
        {
            Include(_planes[word].ScenePlane.Node);
        }
        return new PhraseDrawing(phrase, words, nodes.ToArray());
    }

    private Vector2 HalfViewport => new(_viewport.Width * 0.5f, _viewport.Height * 0.5f);

    private void PrepareTypography()
    {
        if (!_fitDirty)
        {
            return;
        }
        _fitDirty = false;
        var region = (HalfViewport, _overlay?.Feather ?? 0);
        if (region != _fitRegion)
        {
            _fitRegion = region;
            _fits.Clear();
        }

        var active = _phrases.Select(phrase => phrase.Style).ToHashSet();
        foreach (PhraseStyle removed in _fits.Keys.Where(key => !active.Contains(key)).ToArray())
        {
            _fits.Remove(removed);
        }

        foreach (PhraseDrawing phrase in _phrases)
        {
            Vector2 half = HalfViewport;
            float feather = _overlay?.Feather ?? 0;
            float margin = feather + Math.Clamp(half.Y * 0.10f, 5, 22) + 4 + phrase.Style.GlowRadius;
            phrase.Safe = Vector2.Max(new Vector2(4), half - new Vector2(margin));
            if (_fits.TryGetValue(phrase.Style, out PhraseFit previous))
            {
                phrase.Scale = previous.Scale;
                for (int i = 0; i < phrase.Words.Length; i++)
                {
                    _wordPositions[phrase.Words[i]] = previous.Positions[i];
                }
                continue;
            }

            float width = phrase.Style.Width * Math.Clamp((phrase.Safe.X / phrase.Safe.Y) / 1.78f, 0.58f, 1.25f);
            PlaceWords(phrase, width);
            float fit = PlanScale(phrase);
            if (fit < 0.93f && phrase.Words.Length > 2)
            {
                float wider = width * 1.24f;
                PlaceWords(phrase, wider);
                float adjusted = PlanScale(phrase);
                if (adjusted > fit + 0.010f)
                {
                    fit = adjusted;
                }
                else
                {
                    PlaceWords(phrase, width);
                }
            }
            phrase.Scale = fit;
            _fits.Add(phrase.Style, new PhraseFit(fit, phrase.Words.Select(i => _wordPositions[i]).ToArray()));
        }
    }

    private void PlaceWords(PhraseDrawing phrase, float targetWidth)
    {
        const float gap = 0.19f;
        List<List<(int Index, float Width)>> rows = [[]];
        float width = 0;
        foreach (int i in phrase.Words)
        {
            Drawable drawable = _planes[i];
            float height = drawable.ScenePlane.Size.Y;
            float extent = (drawable.Word!.Advance + (drawable.Word.Clusters.Length - 1)
                * drawable.ScenePlane.Word!.MaximumTracking) * height * 1.095f;
            if (width > 0 && width + extent > targetWidth)
            {
                rows.Add([]);
                width = 0;
            }

            rows[^1].Add((i, extent));
            width += extent + gap;
        }

        for (int row = 0; row < rows.Count; row++)
        {
            var words = rows[row];
            float rowWidth = words.Sum(word => word.Width) + gap * (words.Count - 1);
            float stagger = (row - (rows.Count - 1) * 0.5f) * phrase.Style.Stagger;
            float cursor = -rowWidth * 0.5f + stagger;
            float baseline = ((rows.Count - 1) * 0.5f - row) * 0.93f;
            foreach (var word in words)
            {
                _wordPositions[word.Index] = new Vector3(cursor + word.Width * 0.5f, baseline, 0);
                cursor += word.Width + gap;
            }
        }
    }

    private float PlanScale(PhraseDrawing phrase)
    {
        double from = phrase.Style.ReadStart;
        double span = phrase.Style.ReadEnd - from;
        int steps = Math.Clamp((int)Math.Ceiling(span * 20), 5, 80);
        float fit = 1;
        for (int i = 0; i <= steps; i++)
        {
            double t = from + span * i / steps;
            foreach (int index in phrase.Nodes)
            {
                var node = _sequence.Rig.GetNode(index);
                Matrix4x4 local = node.MatrixAt(t);
                _world[index] = node.ParentIndex is int parent ? local * _world[parent] : local;
            }
            Matrix4x4 projection = ViewProjection(_world[_sequence.CameraNode], t, _viewport.Width / _viewport.Height);
            ProjectedInk measured = MeasureInk(phrase, t, projection, reserveTracking: true);
            fit = Math.Min(fit, measured.Fit(measured.Center, phrase.Safe));
        }
        return fit < 1 ? fit * 0.987f : fit;
    }

    private Matrix4x4 WordToClip(int index, Matrix4x4 projection)
    {
        ScenePlane plane = _planes[index].ScenePlane;
        Matrix4x4 pose = _world[plane.Node];
        int parent = _sequence.Rig.GetNode(plane.Node).ParentIndex!.Value;
        pose.Translation += Vector3.TransformNormal(_wordPositions[index], _world[parent]);
        float size = plane.Size.Y;
        return Matrix4x4.CreateScale(size) * pose * projection;
    }

    private ProjectedInk MeasureInk(PhraseDrawing phrase, double t, Matrix4x4 projection, bool reserveTracking)
    {
        var ink = new ProjectedInk();
        foreach (int i in phrase.Words)
        {
            WordStyle style = _planes[i].ScenePlane.Word!;
            var clusters = _planes[i].Word!.Clusters;
            Matrix4x4 transform = WordToClip(i, projection);
            float tracking = reserveTracking ? style.MaximumTracking : style.TrackingAt(t);
            ink.Add(clusters, transform, HalfViewport, tracking, style.Lead);
            if (!reserveTracking)
            {
                _wordTransforms[i] = transform;
            }
        }
        return ink;
    }

    private void FramePhrases(double seconds, Matrix4x4 projection)
    {
        foreach (PhraseDrawing phrase in _phrases)
        {
            phrase.LastInk = default;
            if (seconds < phrase.Style.Start || seconds > phrase.Style.End)
            {
                continue;
            }

            ProjectedInk ink = MeasureInk(phrase, seconds, projection, reserveTracking: false);
            float blend = phrase.Style.FramingAt(seconds);
            FramingTransform correction = FramingTransform.At(ink, ink.Center, phrase.Safe, phrase.Scale, blend, HalfViewport);
            Matrix4x4 frame = correction.ClipMatrix;
            foreach (int i in phrase.Words)
            {
                _wordTransforms[i] *= frame;
            }

            phrase.LastInk = ink;
            phrase.LastCorrection = correction;
        }
    }

    public IEnumerable<ReadingSample> MeasureReading(double seconds)
    {
        PrepareTypography();
        _sequence.Rig.Evaluate(seconds, _world);
        Matrix4x4 projection = ViewProjection(_world[_sequence.CameraNode], seconds, _viewport.Width / _viewport.Height);
        FramePhrases(seconds, projection);
        foreach (PhraseDrawing phrase in _phrases)
        {
            if (!phrase.LastInk.Valid)
            {
                continue;
            }

            FramingTransform correction = phrase.LastCorrection;
            Vector2 translation = correction.Offset * HalfViewport;
            Vector2 min = phrase.LastInk.Min * correction.Scale + translation;
            Vector2 max = phrase.LastInk.Max * correction.Scale + translation;
            float clearance = Math.Min(Math.Min(phrase.Safe.X + min.X, phrase.Safe.X - max.X),
                Math.Min(phrase.Safe.Y + min.Y, phrase.Safe.Y - max.Y));
            Vector2 center = phrase.LastInk.Center * correction.Scale + translation;
            Vector3 camera = _world[_sequence.CameraNode].Translation;
            yield return new ReadingSample(phrase.Style.Index, phrase.Style.Family, phrase.Style.FramingAt(seconds) >= 0.9999f,
                center, min, max, clearance, correction.Scale, camera);
        }
    }

    private readonly record struct PhraseFit(float Scale, Vector3[] Positions);

    private sealed record PhraseDrawing(PhraseStyle Style, int[] Words, int[] Nodes)
    {
        public Vector2 Safe { get; set; }
        public float Scale { get; set; }
        public ProjectedInk LastInk { get; set; }
        public FramingTransform LastCorrection { get; set; }
    }
}

internal readonly record struct ReadingSample(int Cue, MotionFamily Family, bool Reading, Vector2 Center,
    Vector2 Min, Vector2 Max, float Clearance, float Scale, Vector3 Camera);
