using System.Numerics;
using System.Runtime.InteropServices;
using Lenoz.Motion;
using LyricSpace.Studio.Composition;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace LyricSpace.Studio.Rendering;

internal sealed class StageRenderer : IDisposable
{
    internal const Format ColorFormat = Format.B8G8R8A8_UNorm;
    private const int ExposureSamples = 3;
    private static readonly Color4 Letterbox = new(0.018f, 0.018f, 0.022f, 1);

    private readonly List<IDisposable> _resources = new();
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1? _swapChain;
    private readonly IDXGIAdapter3? _memoryAdapter;
    private readonly Sequence _sequence;
    private readonly GpuTimer _timer;
    private readonly ID3D11VertexShader _quadShader;
    private readonly ID3D11VertexShader _screenShader;
    private readonly ID3D11PixelShader _opaqueShader;
    private readonly ID3D11PixelShader _blendShader;
    private readonly ID3D11PixelShader _backdropShader;
    private readonly ID3D11PixelShader _exposureShader;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11BlendState _blend;
    private readonly ID3D11DepthStencilState _depthWrite;
    private readonly ID3D11DepthStencilState _depthRead;
    private readonly ID3D11DepthStencilState _noDepth;
    private readonly ID3D11RasterizerState _rasterizer;
    private readonly Matrix4x4[] _world;
    private readonly Drawable[] _planes;
    private readonly DrawItem[] _visible;

    private ID3D11Texture2D? _backBuffer;
    private ID3D11RenderTargetView? _target;
    private ID3D11DepthStencilView? _depth;
    private SampleTarget[]? _exposureTargets;
    private Viewport _viewport;

    public StageRenderer(Sequence sequence, nint hwnd, int width, int height)
    {
        _sequence = sequence;
        _world = new Matrix4x4[sequence.Rig.Count];
        _visible = new DrawItem[sequence.Planes.Length];

        try
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out _device, out _, out _context).CheckError();
            Own(_device);
            Own(_context);
            using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            AdapterName = adapter.Description.Description.TrimEnd('\0', ' ');
            _memoryAdapter = adapter.QueryInterfaceOrNull<IDXGIAdapter3>();
            if (_memoryAdapter is not null)
            {
                Own(_memoryAdapter);
            }

            if (hwnd != 0)
            {
                using var factory = adapter.GetParent<IDXGIFactory2>();
                _swapChain = Own(factory.CreateSwapChainForHwnd(_device, hwnd, new SwapChainDescription1
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    Format = ColorFormat,
                    SampleDescription = SampleDescription.Default,
                    BufferUsage = Usage.RenderTargetOutput,
                    BufferCount = 2,
                    Scaling = Scaling.Stretch,
                    SwapEffect = SwapEffect.FlipDiscard,
                    AlphaMode = AlphaMode.Ignore
                }));
                factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter).CheckError();
                using var latencyDevice = _device.QueryInterface<IDXGIDevice1>();
                latencyDevice.MaximumFrameLatency = 1;
            }

            _timer = Own(new GpuTimer(_device, _context));
            var masks = Own(new GlyphMasks(_device, _context));
            _planes = new Drawable[sequence.Planes.Length];
            ulong glyphBytes = 0;
            var styles = new HashSet<TextStyle>();
            for (int index = 0; index < _planes.Length; index++)
            {
                ScenePlane ScenePlane = sequence.Planes[index];
                GlyphMask? mask = ScenePlane.Text is { } text ? masks.Get(text) : null;
                if (ScenePlane.Text is { } style && mask is { } glyph && styles.Add(style))
                {
                    glyphBytes += glyph.Bytes;
                }

                Vector2 size = ScenePlane.Size;
                if (mask is { } m)
                {
                    size.Y *= m.PaddingScale;
                    size.X = size.Y * m.Aspect;
                }

                _planes[index] = new Drawable(ScenePlane, Matrix4x4.CreateScale(size.X, size.Y, 1), mask?.View);
            }

            GlyphBytes = glyphBytes;
            using var shaderStream = typeof(StageRenderer).Assembly.GetManifestResourceStream("LyricSpace.Studio.Rendering.Stage.hlsl")
                ?? throw new InvalidOperationException("The stage shader resource is missing.");
            using var reader = new StreamReader(shaderStream);
            string shader = reader.ReadToEnd();

            _quadShader = Own(_device.CreateVertexShader(Compile(shader, "QuadVS", "vs_5_0").Span));
            _screenShader = Own(_device.CreateVertexShader(Compile(shader, "ScreenVS", "vs_5_0").Span));
            _opaqueShader = Own(_device.CreatePixelShader(Compile(shader, "OpaquePS", "ps_5_0").Span));
            _blendShader = Own(_device.CreatePixelShader(Compile(shader, "BlendPS", "ps_5_0").Span));
            _backdropShader = Own(_device.CreatePixelShader(Compile(shader, "BackdropPS", "ps_5_0").Span));
            _exposureShader = Own(_device.CreatePixelShader(Compile(shader, "ExposurePS", "ps_5_0").Span));
            _constants = Own(_device.CreateConstantBuffer<DrawConstants>());
            _sampler = Own(_device.CreateSamplerState(new SamplerDescription(Filter.Anisotropic, TextureAddressMode.Clamp,
                TextureAddressMode.Clamp, TextureAddressMode.Clamp, maxAnisotropy: 8)));
            _blend = Own(_device.CreateBlendState(BlendDescription.AlphaBlend));
            _depthWrite = Own(_device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.All, ComparisonFunction.LessEqual)));
            _depthRead = Own(_device.CreateDepthStencilState(new DepthStencilDescription(true, DepthWriteMask.Zero, ComparisonFunction.LessEqual)));
            _noDepth = Own(_device.CreateDepthStencilState(DepthStencilDescription.None));
            _rasterizer = Own(_device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)));
            Resize(width, height);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string AdapterName { get; }
    public ulong GlyphBytes { get; }
    public ulong? LocalVideoMemory => _memoryAdapter?.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local).CurrentUsage;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public ID3D11Texture2D Output => _backBuffer ?? throw new InvalidOperationException("There is no render surface.");
    public ID3D11Device Device => _device;
    public ID3D11DeviceContext Context => _context;

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width == Width && height == Height))
        {
            return;
        }

        _context.ClearState();
        ReleaseTargets();
        if (_swapChain is not null)
        {
            if (Width != 0)
            {
                _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None).CheckError();
            }

            _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        }
        else
        {
            _backBuffer = _device.CreateTexture2D(ColorFormat, (uint)width, (uint)height, bindFlags: BindFlags.RenderTarget);
        }

        _target = _device.CreateRenderTargetView(_backBuffer);
        using var texture = _device.CreateTexture2D(Format.D32_Float, (uint)width, (uint)height, bindFlags: BindFlags.DepthStencil);
        _depth = _device.CreateDepthStencilView(texture);
        Width = width;
        Height = height;

        int videoWidth = Math.Min(width, (int)Math.Round(height * Sequence.Aspect));
        int videoHeight = Math.Min(height, (int)Math.Round(width / Sequence.Aspect));
        _viewport = new Viewport((width - videoWidth) / 2, (height - videoHeight) / 2, Math.Max(1, videoWidth), Math.Max(1, videoHeight));
    }

    public void Draw(double seconds, bool motionBlur, long frame, Action<GpuSample>? measure = null)
    {
        ID3D11RenderTargetView target = _target ?? throw new InvalidOperationException("There is no render target.");
        if (motionBlur)
        {
            _exposureTargets ??= CreateExposureTargets();
        }
        else
        {
            ReleaseExposureTargets();
        }

        if (measure is not null)
        {
            _timer.ReadCompleted(measure);
            _timer.Begin(frame);
        }

        _context.RSSetState(_rasterizer);
        _context.RSSetViewport(_viewport);
        _context.VSSetConstantBuffer(0, _constants);
        _context.PSSetConstantBuffer(0, _constants);
        _context.PSSetSampler(0, _sampler);

        if (_exposureTargets is { } exposure)
        {
            // Three midpoint samples across a half-frame (180-degree) exposure.
            double step = 0.5 / Sequence.ReferenceFps / ExposureSamples;
            for (int index = 0; index < exposure.Length; index++)
            {
                double sampleTime = Math.Clamp(seconds + (index - 1) * step, 0, _sequence.Duration);
                DrawSample(exposure[index].Target, sampleTime);
            }

            _context.OMSetRenderTargets(target);
            _context.ClearRenderTargetView(target, Letterbox);
            _context.OMSetBlendState(null);
            _context.OMSetDepthStencilState(_noDepth);
            _context.PSSetShaderResource(0, exposure[0].Resource);
            _context.PSSetShaderResource(1, exposure[1].Resource);
            _context.PSSetShaderResource(2, exposure[2].Resource);
            DrawScreen(_exposureShader);
            _context.PSUnsetShaderResources(0, ExposureSamples);
        }
        else
        {
            DrawSample(target, seconds);
        }

        if (measure is not null)
        {
            _timer.End();
        }
    }

    public bool Present()
    {
        Result result = _swapChain?.Present(1, PresentFlags.None) ?? Result.Ok;
        result.CheckError();
        return result == Result.Ok;
    }

    public uint? PresentedCount => _swapChain is not null && _swapChain.GetFrameStatistics(out FrameStatistics stats) == Result.Ok
        ? stats.PresentCount
        : null;

    private void DrawSample(ID3D11RenderTargetView target, double seconds)
    {
        ID3D11DepthStencilView depth = _depth!;
        _context.ClearRenderTargetView(target, Letterbox);
        _context.ClearDepthStencilView(depth, DepthStencilClearFlags.Depth, 1, 0);
        _context.OMSetRenderTargets(target, depth);
        _context.OMSetBlendState(null);
        _context.OMSetDepthStencilState(_noDepth);
        SetConstants(new DrawConstants(Matrix4x4.Identity, Vector4.Zero, new Vector4((float)_sequence.Temperature.At(seconds), 0, 0, 0)));
        DrawScreen(_backdropShader);

        _sequence.Rig.Evaluate(seconds, _world);
        var camera = _world[_sequence.CameraNode];
        float fov = PerspectiveCamera.VerticalFovFromZoom(1080, (float)_sequence.Zoom.At(seconds));
        Matrix4x4 projection = PerspectiveCamera.ViewProjection(camera, fov, Sequence.Aspect, 0.08f, 85);
        Vector3 viewAxis = new(camera.M31, camera.M32, camera.M33);
        int count = 0;
        for (int index = 0; index < _planes.Length; index++)
        {
            Drawable drawable = _planes[index];
            float opacity = drawable.ScenePlane.Opacity(seconds);
            if (opacity <= 0.001f)
            {
                continue;
            }

            Matrix4x4 world = _world[drawable.ScenePlane.Node];
            float viewDepth = Vector3.Dot(world.Translation - camera.Translation, viewAxis);
            _visible[count++] = new DrawItem(index, opacity, viewDepth);
        }

        Array.Sort(_visible, 0, count);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        _context.VSSetShader(_quadShader);

        _context.OMSetDepthStencilState(_depthWrite);
        _context.PSSetShader(_opaqueShader);
        for (int index = count - 1; index >= 0; index--)
        {
            DrawItem item = _visible[index];
            if (item.Opacity >= 0.999f)
            {
                DrawQuad(item, projection);
            }
        }

        _context.OMSetDepthStencilState(_depthRead);
        _context.OMSetBlendState(_blend);
        _context.PSSetShader(_blendShader);
        for (int index = 0; index < count; index++)
        {
            DrawQuad(_visible[index], projection);
        }

        _context.PSUnsetShaderResources(0, 1);
    }

    private void DrawQuad(DrawItem item, Matrix4x4 projection)
    {
        Drawable drawable = _planes[item.Index];
        if (drawable.Mask is { } mask)
        {
            _context.PSSetShaderResource(0, mask);
        }

        SetConstants(new DrawConstants(drawable.Size * _world[drawable.ScenePlane.Node] * projection,
            new Vector4(drawable.ScenePlane.Color, item.Opacity), new Vector4(0, drawable.Mask is null ? 0 : 1, 0, 0)));
        _context.Draw(4, 0);
    }

    private void DrawScreen(ID3D11PixelShader shader)
    {
        _context.VSSetShader(_screenShader);
        _context.PSSetShader(shader);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.Draw(3, 0);
    }

    private void SetConstants(DrawConstants data)
    {
        MappedSubresource mapping = _context.Map(_constants, MapMode.WriteDiscard);
        mapping.AsSpan<DrawConstants>(1)[0] = data;
        _context.Unmap(_constants, 0);
    }

    private static ReadOnlyMemory<byte> Compile(string shader, string entry, string profile)
        => Compiler.Compile(shader, entry, "Stage.hlsl", profile, ShaderFlags.OptimizationLevel3);

    private SampleTarget[] CreateExposureTargets()
    {
        var targets = new List<SampleTarget>(ExposureSamples);
        try
        {
            for (int index = 0; index < ExposureSamples; index++)
            {
                using var surface = _device.CreateTexture2D(ColorFormat, (uint)Width, (uint)Height,
                    bindFlags: BindFlags.RenderTarget | BindFlags.ShaderResource);
                var target = _device.CreateRenderTargetView(surface);
                try
                {
                    targets.Add(new SampleTarget(target, _device.CreateShaderResourceView(surface)));
                }
                catch
                {
                    target.Dispose();
                    throw;
                }
            }

            return targets.ToArray();
        }
        catch
        {
            foreach (SampleTarget target in targets)
            {
                target.Dispose();
            }

            throw;
        }
    }

    private void ReleaseExposureTargets()
    {
        if (_exposureTargets is null)
        {
            return;
        }

        foreach (SampleTarget target in _exposureTargets)
        {
            target.Dispose();
        }

        _exposureTargets = null;
    }

    private void ReleaseTargets()
    {
        ReleaseExposureTargets();
        _depth?.Dispose();
        _target?.Dispose();
        _backBuffer?.Dispose();
        _target = null;
        _depth = null;
        _backBuffer = null;
    }

    private T Own<T>(T resource) where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }

    public void Dispose()
    {
        if (_resources.Count == 0)
        {
            return;
        }

        ReleaseTargets();
        _context?.ClearState();
        _context?.Flush();
        for (int index = _resources.Count - 1; index >= 0; index--)
        {
            _resources[index].Dispose();
        }

        _resources.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DrawConstants(Matrix4x4 Transform, Vector4 Tint, Vector4 Parameters);

    private readonly record struct Drawable(ScenePlane ScenePlane, Matrix4x4 Size, ID3D11ShaderResourceView? Mask);
    private readonly record struct DrawItem(int Index, float Opacity, float Depth) : IComparable<DrawItem>
    {
        public int CompareTo(DrawItem other)
        {
            int depthOrder = Depth.CompareTo(other.Depth);
            return depthOrder == 0 ? Index.CompareTo(other.Index) : depthOrder;
        }
    }

    private sealed record SampleTarget(ID3D11RenderTargetView Target, ID3D11ShaderResourceView Resource) : IDisposable
    {
        public void Dispose()
        {
            Resource.Dispose();
            Target.Dispose();
        }
    }
}
