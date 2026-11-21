using System.Numerics;
using LyricSpace.Studio.Composition;
using SharpGen.Runtime;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace LyricSpace.Studio.Rendering;

/// <summary>One cached atlas and instance buffer per shaped word.</summary>
internal sealed class WordMasks(ID3D11Device device, ID3D11DeviceContext context) : IDisposable
{
    private const float Em = 128;
    private const int Padding = 50;
    private const int MaxClusters = 64;
    private readonly ID2D1Factory _drawing = D2D1.D2D1CreateFactory<ID2D1Factory>();
    private readonly IDWriteFactory _writing = DWrite.DWriteCreateFactory<IDWriteFactory>();
    private readonly Dictionary<TextStyle, WordMask?> _cache = new();

    public ulong Bytes => _cache.Values.Aggregate(0UL, (sum, mask) => sum + (mask?.Bytes ?? 0));

    public bool Warm(TextStyle style)
    {
        if (_cache.ContainsKey(style))
        {
            return false;
        }

        _ = Get(style);
        return true;
    }

    public WordMask? Get(TextStyle style)
    {
        if (!_cache.TryGetValue(style, out WordMask? mask))
        {
            _cache.Add(style, mask = Build(style));
        }
        return mask;
    }

    public void Retain(HashSet<TextStyle> styles)
    {
        foreach (TextStyle style in _cache.Keys.Where(style => !styles.Contains(style)).ToArray())
        {
            _cache.Remove(style, out WordMask? mask);
            mask?.Dispose();
        }
    }

    private WordMask? Build(TextStyle style)
    {
        using var format = _writing.CreateTextFormat(style.Family!, style.Face == Typeface.Display ? FontWeight.Black : FontWeight.SemiBold,
            Vortice.DirectWrite.FontStyle.Normal, Em);
        format.WordWrapping = WordWrapping.NoWrap;
        using var layout = _writing.CreateTextLayout(style.Text, format, 8192, 320);
        using var outline = new OutlineRenderer(_drawing);
        layout.Draw(0, outline, 0, 0);
        var cells = outline.Cells.OrderBy(cell => cell.Origin.X + cell.Bounds.Left).ToArray();
        if (cells.Length is 0 or > MaxClusters)
        {
            return null;
        }

        float centre = layout.Metrics.Width * 0.5f;
        int width = Math.Max(1024, cells.Max(c => c.Width));
        int x = 0, y = 0, row = 0, usedWidth = 0;
        foreach (Cell cell in cells)
        {
            if (x + cell.Width > width)
            {
                x = 0;
                y += row;
                row = 0;
            }

            cell.X = x;
            cell.Y = y;
            x += cell.Width;
            usedWidth = Math.Max(usedWidth, x);
            row = Math.Max(row, cell.Height);
        }
        int height = y + row;
        width = usedWidth;
        if (width > 8192 || height > 8192)
        {
            return null;
        }

        const Format pixelFormat = Format.B8G8R8A8_UNorm;
        using var source = device.CreateTexture2D(pixelFormat, (uint)width, (uint)height, bindFlags: BindFlags.RenderTarget);
        using (var surface = source.QueryInterface<IDXGISurface>())
        using (var target = _drawing.CreateDxgiSurfaceRenderTarget(surface, new RenderTargetProperties
        {
            PixelFormat = new PixelFormat(pixelFormat, Vortice.DCommon.AlphaMode.Premultiplied), DpiX = 96, DpiY = 96
        }))
        using (var ink = target.CreateSolidColorBrush(Colors.White))
        {
            target.BeginDraw();
            target.Clear(new Color4(0, 0, 0, 0));
            foreach (Cell cell in cells)
            {
                target.Transform = Matrix3x2.CreateTranslation(cell.X + Padding - cell.Bounds.Left, cell.Y + Padding - cell.Bounds.Top);
                target.FillGeometry(cell.Outline, ink);
            }
            target.EndDraw().CheckError();
        }

        var quads = new ClusterQuad[MaxClusters];
        var shaped = new InkCluster[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            Cell cell = cells[i];
            Vector2 local = new(cell.Origin.X - centre, outline.Baseline - cell.Origin.Y);
            Vector2 ToEm(Vector2 point) => new((local.X + point.X) / Em, (local.Y - point.Y) / Em);
            Vector2 corner = ToEm(new Vector2(cell.Bounds.Left - Padding, cell.Bounds.Top - Padding));
            quads[i] = new ClusterQuad(
                new Vector4(corner.X, corner.Y, cell.Width / Em, -cell.Height / Em),
                new Vector4((float)cell.X / width, (float)cell.Y / height, (float)cell.Width / width, (float)cell.Height / height));
            shaped[i] = new InkCluster(cell.Moments.Hull().Select(ToEm).ToArray(),
                ToEm(cell.Moments.Centroid), cell.Moments.Area / (Em * Em), i - (cells.Length - 1) * 0.5f);
        }

        using var texture = device.CreateTexture2D(pixelFormat, (uint)width, (uint)height, mipLevels: 0,
            bindFlags: BindFlags.ShaderResource | BindFlags.RenderTarget, miscFlags: ResourceOptionFlags.GenerateMips);
        var view = device.CreateShaderResourceView(texture);
        try
        {
            context.CopySubresourceRegion(texture, 0, 0, 0, 0, source, 0);
            context.GenerateMips(view);
            var instances = device.CreateBuffer<ClusterQuad>(quads, BindFlags.ConstantBuffer, ResourceUsage.Immutable);
            return new WordMask(view, instances, shaped, layout.Metrics.Width / Em,
                (ulong)width * (ulong)height * 4 * 4 / 3 + MaxClusters * 32);
        }
        catch
        {
            view.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (WordMask? mask in _cache.Values)
        {
            mask?.Dispose();
        }
        _cache.Clear();
        _writing.Dispose();
        _drawing.Dispose();
    }

    private readonly record struct ClusterQuad(Vector4 Local, Vector4 Texture);

    private sealed record Cell(ID2D1PathGeometry Outline, Vortice.RawRectF Bounds, Vector2 Origin, OutlineMoments Moments)
    {
        public int Width => (int)MathF.Ceiling(Bounds.Right - Bounds.Left) + Padding * 2;
        public int Height => (int)MathF.Ceiling(Bounds.Bottom - Bounds.Top) + Padding * 2;
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class OutlineRenderer(ID2D1Factory factory) : TextRendererBase
    {
        public List<Cell> Cells { get; } = [];
        public float Baseline { get; private set; } = float.NaN;

        public override RawBool IsPixelSnappingDisabled(nint context) => true;

        public override unsafe void DrawGlyphRun(nint context, float x, float y, MeasuringMode mode, GlyphRun run,
            GlyphRunDescription description, IUnknown drawingEffect)
        {
            if (float.IsNaN(Baseline))
            {
                Baseline = y;
            }

            if (run.Indices is not { Length: > 0 } indices || run.Advances is not { } advances || run.FontFace is not { } font)
            {
                return;
            }

            var offsets = run.Offsets ?? new GlyphOffset[indices.Length];
            bool rtl = (run.BidiLevel & 1) != 0;
            var starts = new SortedSet<int> { 0, indices.Length };
            if (description.ClusterMap != 0)
            {
                var clusters = new ReadOnlySpan<ushort>((void*)description.ClusterMap, description.Text?.Length ?? 0);
                foreach (ushort cluster in clusters)
                {
                    if (cluster < indices.Length)
                    {
                        starts.Add(cluster);
                    }
                }
            }

            float origin = x;
            int first = 0;
            foreach (int last in starts.Skip(1))
            {
                var geometry = factory.CreatePathGeometry();
                try
                {
                    using (var sink = geometry.Open())
                    {
                        font.GetGlyphRunOutline(run.FontEmSize, indices[first..last], advances[first..last],
                            offsets[first..last], run.IsSideways, rtl, sink);
                        sink.Close();
                    }

                    var bounds = geometry.GetBounds();
                    if (bounds.Right > bounds.Left && bounds.Bottom > bounds.Top)
                    {
                        using var moment = new OutlineMoments();
                        geometry.Simplify(GeometrySimplificationOption.Lines, 0.30f, moment);
                        if (moment.Area > 0)
                        {
                            Cells.Add(new Cell(geometry, bounds, new Vector2(origin, y), moment));
                            geometry = null!;
                        }
                    }
                }
                finally
                {
                    geometry?.Dispose();
                }

                foreach (float advance in advances.AsSpan(first, last - first))
                {
                    origin += rtl ? -advance : advance;
                }
                first = last;
            }
        }

        protected override void DisposeCore(bool disposing)
        {
            if (disposing)
            {
                foreach (Cell cell in Cells)
                {
                    cell.Outline.Dispose();
                }
            }
            base.DisposeCore(disposing);
        }
    }

    private sealed class OutlineMoments : CallbackBase, ID2D1SimplifiedGeometrySink
    {
        private readonly List<Vector2> _points = [];
        private Vector2 _first;
        private Vector2 _last;
        private double _twiceArea;
        private Vector2 _moment;

        public float Area => (float)Math.Abs(_twiceArea * 0.5);
        public Vector2 Centroid => _moment / (float)(3 * _twiceArea);

        public void SetFillMode(Vortice.Direct2D1.FillMode mode) { }
        public void SetSegmentFlags(PathSegment flags) { }
        public void BeginFigure(Vector2 start, FigureBegin begin)
        {
            _points.Add(start);
            _first = _last = start;
        }
        public void AddLines(Vector2[] points)
        {
            foreach (Vector2 point in points)
            {
                _points.Add(point);
                Edge(point);
            }
        }
        public void AddBeziers(BezierSegment[] segments)
        {
            throw new InvalidOperationException("The simplified outline must contain lines.");
        }
        public void EndFigure(FigureEnd end) => Edge(_first);
        public void Close() { }

        private void Edge(Vector2 point)
        {
            float cross = _last.X * point.Y - _last.Y * point.X;
            _twiceArea += cross;
            _moment += (_last + point) * cross;
            _last = point;
        }

        public Vector2[] Hull()
        {
            var sorted = _points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
            var hull = new List<Vector2>();
            void Append(Vector2 point, int minimum)
            {
                while (hull.Count >= minimum)
                {
                    Vector2 a = hull[^1] - hull[^2];
                    Vector2 b = point - hull[^1];
                    if (a.X * b.Y > a.Y * b.X)
                    {
                        break;
                    }
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(point);
            }

            foreach (Vector2 point in sorted)
            {
                Append(point, 2);
            }
            int threshold = hull.Count + 1;
            for (int i = sorted.Length - 2; i >= 0; i--)
            {
                Append(sorted[i], threshold);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull.ToArray();
        }
    }
}

internal sealed record WordMask(ID3D11ShaderResourceView View, ID3D11Buffer Instances, InkCluster[] Clusters, float Advance, ulong Bytes) : IDisposable
{
    public void Dispose()
    {
        Instances.Dispose();
        View.Dispose();
    }
}
