using System.Globalization;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Diagnostics;
using LyricSpace.Studio.Overlay;
using LyricSpace.Studio.Lyrics;
using LyricSpace.Studio.Playback;

namespace LyricSpace.Studio;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool diagnostic = args.Any(arg => arg is "--capture" or "--benchmark" or "--overlay-benchmark" or "--trace");
        try
        {
            uint seed = DemoSequence.DefaultSeed;
            bool motionBlur = true;
            bool studio = false;
            bool demo = false;
            string? mediaLog = null;
            string? preset = null;
            string? customAppearance = null;
            string? localLrc = null;
            bool loop = false;
            string? capture = null;
            string? benchmark = null;
            string? overlayBenchmark = null;
            string? trace = null;
            string? evidence = null;
            string? region = null;
            float? feather = null;
            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--seed":
                        seed = uint.Parse(Argument(args, ref index), CultureInfo.InvariantCulture);
                        break;
                    case "--ae-reference":
                        evidence = Argument(args, ref index);
                        break;
                    case "--trace":
                        trace = Argument(args, ref index);
                        break;
                    case "--capture":
                        capture = Argument(args, ref index);
                        break;
                    case "--benchmark":
                        benchmark = Argument(args, ref index);
                        break;
                    case "--overlay-benchmark":
                        overlayBenchmark = Argument(args, ref index);
                        break;
                    case "--region":
                        region = Argument(args, ref index);
                        break;
                    case "--feather":
                        feather = float.Parse(Argument(args, ref index), CultureInfo.InvariantCulture);
                        break;
                    case "--demo":
                        demo = true;
                        break;
                    case "--media-log":
                        mediaLog = Argument(args, ref index);
                        break;
                    case "--look":
                        preset = Argument(args, ref index);
                        break;
                    case "--appearance":
                        customAppearance = Argument(args, ref index);
                        break;
                    case "--lrc":
                        localLrc = Argument(args, ref index);
                        break;
                    case "--studio":
                        studio = true;
                        break;
                    case "--loop":
                        loop = true;
                        break;
                    case "--no-blur":
                        motionBlur = false;
                        break;
                    default:
                        throw new ArgumentException($"Opção desconhecida: {args[index]}");
                }
            }

            if (new[] { capture, benchmark, overlayBenchmark, trace }.Count(path => path is not null) > 1)
            {
                throw new ArgumentException("Escolha uma operação de diagnóstico por execução.");
            }

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyricSpace");
            var preferences = new AppearancePreferences(folder);
            bool offline = studio || capture is not null || trace is not null || benchmark is not null || overlayBenchmark is not null;
            if (localLrc is not null && (!offline || demo || loop || evidence is not null))
            {
                throw new ArgumentException("Use --lrc com --studio, --capture, --trace ou um benchmark.");
            }

            SongLook look = SongLook.Default;
            if (!offline || localLrc is not null)
            {
                look = preset is null && customAppearance is null ? preferences.ReadDefault() : preferences.Read(preset, customAppearance);
            }
            ProceduralSong? song = localLrc is null ? null : ReadSong(localLrc, seed, look);
            Sequence sequence = song?.SceneAt(0) ?? (evidence is null ? DemoSequence.Create(seed) : ReferenceSequence.Create(evidence));
            if (trace is not null)
            {
                if (song is null)
                {
                    MotionTrace.Write(sequence, trace);
                }
                else
                {
                    MotionTrace.WriteReading(song, trace, OverlayLayout.Fit(new Size(1280, 720), region, feather));
                }
            }
            else if (capture is not null)
            {
                OverlayLayout? captureLayout = region is not null || feather is not null
                    ? OverlayLayout.Fit(new Size(1280, 720), region, feather) : null;
                FrameCapture.Run(sequence, motionBlur, capture, song, captureLayout);
            }
            else if (benchmark is not null)
            {
                using var form = new BenchmarkForm(sequence, motionBlur, benchmark, song: song);
                Application.Run(form);
                return form.Completed ? 0 : 1;
            }
            else if (studio)
            {
                using var form = new StudioForm(sequence, motionBlur, song);
                Application.Run(form);
            }
            else
            {
                Rectangle screen = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Monitor primário indisponível.");
                OverlayLayout layout = OverlayLayout.Fit(screen.Size, region, feather);
                if (overlayBenchmark is not null)
                {
                    using var form = new BenchmarkForm(sequence, motionBlur, overlayBenchmark, layout, song);
                    Application.Run(form);
                    return form.Completed ? 0 : 1;
                }

                using var live = demo || loop || evidence is not null ? null : new LivePlayback(
                    Path.Combine(folder, "lyrics-cache"), Path.Combine(folder, "lyrics"), seed, look, mediaLog);
                using var overlay = new OverlayWindow(sequence, motionBlur, loop, screen, layout, live, preferences);
                Application.Run(overlay);
            }

            return 0;
        }
        catch (Exception error)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyricSpace");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "studio-error.log"), error.ToString());
            if (diagnostic)
            {
                Console.Error.WriteLine(error);
            }
            else
            {
                MessageBox.Show(error.Message, "LyricSpace", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return 1;
        }
    }

    private static ProceduralSong ReadSong(string path, uint seed, SongLook look)
    {
        if (new FileInfo(path).Length > 4L * LrcParser.MaxCharacters)
        {
            throw new ArgumentException("O arquivo de letras excede o tamanho suportado.");
        }

        var cues = LrcParser.Parse(File.ReadAllText(path));
        if (!cues.Any(cue => cue.Text.Length > 0))
        {
            throw new ArgumentException("O arquivo precisa conter pelo menos uma linha LRC com timestamp.");
        }

        double tail = cues[^1].Text.Length == 0 ? 0.25 : 4;
        double duration = Math.Max(cues[^1].Seconds + tail, cues[^1].Words?[^1].End ?? 0);
        return new ProceduralSong(cues, duration, seed, look);
    }

    private static string Argument(string[] args, ref int index)
        => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"Falta o valor de {args[index]}.");
}
