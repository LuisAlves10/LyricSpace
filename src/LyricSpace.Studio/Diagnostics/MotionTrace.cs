using System.Globalization;
using System.Numerics;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Overlay;
using LyricSpace.Studio.Rendering;

namespace LyricSpace.Studio.Diagnostics;

internal static class MotionTrace
{
    public static void WriteReading(ProceduralSong song, string path, OverlayLayout layout)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var renderer = new StageRenderer(song.SceneAt(0)!, 0, 1280, 720, layout);
        using var writer = new StreamWriter(path);
        writer.WriteLine("frame,seconds,cue,family,reading,center_x,center_y,min_x,min_y,max_x,max_y,clearance_px,scale,cam_x,cam_y,cam_z");
        int frames = (int)Math.Round(song.Duration * Sequence.ReferenceFps);
        for (int frame = 0; frame <= frames; frame++)
        {
            double t = frame / Sequence.ReferenceFps;
            renderer.SetSequence(song.SceneAt(t)!);
            foreach (var sample in renderer.MeasureReading(t))
            {
                writer.WriteLine(FormattableString.Invariant($"{frame},{t:G17},{sample.Cue},{sample.Family},{(sample.Reading ? 1 : 0)},{sample.Center.X:G9},{sample.Center.Y:G9},{sample.Min.X:G9},{sample.Min.Y:G9},{sample.Max.X:G9},{sample.Max.Y:G9},{sample.Clearance:G9},{sample.Scale:G9},{sample.Camera.X:G9},{sample.Camera.Y:G9},{sample.Camera.Z:G9}"));
            }
        }
    }

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
