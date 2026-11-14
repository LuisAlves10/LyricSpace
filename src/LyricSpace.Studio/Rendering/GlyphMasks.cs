using System.Numerics;
using LyricSpace.Studio.Composition;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace LyricSpace.Studio.Rendering;

internal sealed class GlyphMasks(ID3D11Device device, ID3D11DeviceContext context) : IDisposable
{
    private const float Em = 288;
    private const float LayoutWidth = 4096;
    private const float LayoutHeight = 640;
    private const int Padding = 20;
    private readonly Dictionary<TextStyle, GlyphMask> _masks = new();
    private readonly ID2D1Factory _drawFactory = D2D1.D2D1CreateFactory<ID2D1Factory>();
    private readonly IDWriteFactory _textFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();

    public GlyphMask Get(TextStyle style)
    {
        if (_masks.TryGetValue(style, out GlyphMask existing))
        {
            return existing;
        }

        GlyphMask mask = Create(style);
        _masks.Add(style, mask);
        return mask;
    }

    private GlyphMask Create(TextStyle style)
    {
        using IDWriteTextFormat format = style.Face == Typeface.Display
            ? _textFactory.CreateTextFormat("Arial", FontWeight.Black, Vortice.DirectWrite.FontStyle.Normal, Em)
            : _textFactory.CreateTextFormat("Bahnschrift", FontWeight.SemiBold, Vortice.DirectWrite.FontStyle.Normal, FontStretch.SemiCondensed, Em);
        format.WordWrapping = WordWrapping.NoWrap;

        using IDWriteTextLayout layout = _textFactory.CreateTextLayout(style.Text, format, LayoutWidth, LayoutHeight);
        using (var spacing = layout.QueryInterface<IDWriteTextLayout1>())
        {
            float halfTracking = Em * style.Tracking * 0.5f;
            spacing.SetCharacterSpacing(halfTracking, halfTracking, 0, new TextRange(0, (uint)style.Text.Length));
        }

        OverhangMetrics bounds = layout.OverhangMetrics;
        float inkWidth = LayoutWidth + bounds.Left + bounds.Right;
        float inkHeight = LayoutHeight + bounds.Top + bounds.Bottom;
        if (!(inkWidth > 0 && inkWidth <= LayoutWidth && inkHeight > 0 && inkHeight <= LayoutHeight))
        {
            throw new InvalidOperationException("Text is outside the supported single-line glyph mask bounds.");
        }

        uint width = (uint)MathF.Ceiling(inkWidth) + Padding * 2;
        uint height = (uint)MathF.Ceiling(inkHeight) + Padding * 2;
        const Format pixelFormat = Format.B8G8R8A8_UNorm;
        using var source = device.CreateTexture2D(pixelFormat, width, height, bindFlags: BindFlags.RenderTarget);
        using (var surface = source.QueryInterface<IDXGISurface>())
        using (var target = _drawFactory.CreateDxgiSurfaceRenderTarget(surface, new RenderTargetProperties
        {
            PixelFormat = new PixelFormat(pixelFormat, Vortice.DCommon.AlphaMode.Premultiplied),
            DpiX = 96,
            DpiY = 96
        }))
        using (var white = target.CreateSolidColorBrush(Colors.White))
        {
            target.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
            target.BeginDraw();
            target.Clear(new Color4(0, 0, 0, 0));
            target.DrawTextLayout(new Vector2(Padding + bounds.Left, Padding + bounds.Top), layout, white);
            target.EndDraw().CheckError();
        }

        using var mipmapped = device.CreateTexture2D(pixelFormat, width, height, mipLevels: 0,
            bindFlags: BindFlags.ShaderResource | BindFlags.RenderTarget, miscFlags: ResourceOptionFlags.GenerateMips);
        var view = device.CreateShaderResourceView(mipmapped);
        context.CopySubresourceRegion(mipmapped, 0, 0, 0, 0, source, 0);
        context.GenerateMips(view);
        return new GlyphMask(view, (float)width / height, height / inkHeight, TextureBytes(width, height));
    }

    private static ulong TextureBytes(uint width, uint height)
    {
        ulong bytes = 0;
        for (;;)
        {
            bytes += (ulong)width * height * 4;
            if (width == 1 && height == 1)
            {
                return bytes;
            }

            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
    }

    public void Dispose()
    {
        foreach (GlyphMask mask in _masks.Values)
        {
            mask.View.Dispose();
        }

        _masks.Clear();
        _textFactory.Dispose();
        _drawFactory.Dispose();
    }
}

internal readonly record struct GlyphMask(ID3D11ShaderResourceView View, float Aspect, float PaddingScale, ulong Bytes);
