using System.Drawing.Imaging;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Overlay;
using LyricSpace.Studio.Rendering;
using Vortice.Direct3D11;

namespace LyricSpace.Studio.Diagnostics;

internal static class FrameCapture
{
    public static void Run(Sequence sequence, bool motionBlur, string directory, ProceduralSong? song = null, OverlayLayout? layout = null)
    {
        const int width = 1280;
        const int height = 720;
        const int fps = 60;
        Directory.CreateDirectory(directory);
        if (Directory.EnumerateFiles(directory, "frame_*.png").Any())
        {
            throw new IOException("A pasta de captura já contém frames. Escolha uma pasta vazia.");
        }

        using var renderer = new StageRenderer(sequence, 0, width, height, layout);
        using var staging = renderer.Device.CreateTexture2D(StageRenderer.ColorFormat, width, height, bindFlags: BindFlags.None,
            usage: ResourceUsage.Staging, cpuAccessFlags: CpuAccessFlags.Read);
        using var bitmap = new Bitmap(width, height, layout is null ? PixelFormat.Format32bppRgb : PixelFormat.Format32bppPArgb);

        int lastFrame = (int)Math.Ceiling(sequence.Duration * fps);
        for (int frame = 0; frame <= lastFrame; frame++)
        {
            double time = Math.Min(sequence.Duration, frame / (double)fps);
            if (song?.SceneAt(time) is { } scene)
            {
                renderer.SetSequence(scene);
            }
            renderer.Draw(time, motionBlur, frame);
            renderer.Context.CopyResource(staging, renderer.Output);
            SaveMappedFrame(renderer.Context, staging, bitmap, Path.Combine(directory, $"frame_{frame:D4}.png"));
        }
    }

    private static unsafe void SaveMappedFrame(ID3D11DeviceContext context, ID3D11Texture2D staging, Bitmap bitmap, string path)
    {
        var mapping = context.Map(staging, 0, MapMode.Read);
        try
        {
            var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData pixels = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                int rowBytes = bitmap.Width * 4;
                for (int row = 0; row < bitmap.Height; row++)
                {
                    byte* source = (byte*)mapping.DataPointer + row * mapping.RowPitch;
                    byte* destination = (byte*)pixels.Scan0 + row * pixels.Stride;
                    Buffer.MemoryCopy(source, destination, rowBytes, rowBytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(pixels);
            }
        }
        finally
        {
            context.Unmap(staging, 0);
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}
