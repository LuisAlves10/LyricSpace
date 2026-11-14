using System.Globalization;
using System.Numerics;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Rendering;

namespace LyricSpace.Studio.Diagnostics;

internal static class MotionTrace
{
    public static void Write(Sequence sequence, string path)
    {
        const int width = 1920;
        const int height = 1080;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var renderer = new StageRenderer(sequence, 0, width, height);
        using var writer = new StreamWriter(path);
        writer.WriteLine("frame,seconds,text,opacity,visible,cam_x,cam_y,cam_z,cx,cy,p0x,p0y,p1x,p1y,p2x,p2y,p3x,p3y");

        int frames = (int)Math.Round(sequence.Duration * Sequence.ReferenceFps);
        for (int frame = 0; frame <= frames; frame++)
        {
            double t = frame / Sequence.ReferenceFps;
            foreach (ProjectedWord word in renderer.MeasureWords(t))
            {
                Vector3 camera = word.Camera;
                string name = "\""+word.Text.Replace("\"", "\"\"")+"\"";
                writer.Write(FormattableString.Invariant($"{frame},{t:G17},{name},{word.Opacity:G9},{(word.Visible ? 1 : 0)},{camera.X:G9},{camera.Y:G9},{camera.Z:G9},{word.Center.X:G9},{word.Center.Y:G9}"));
                foreach (Vector2 point in word.Corners)
                {
                    writer.Write(string.Create(CultureInfo.InvariantCulture, $",{point.X:G9},{point.Y:G9}"));
                }

                writer.WriteLine();
            }
        }
    }
}
