using System.Globalization;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Diagnostics;
using LyricSpace.Studio.Overlay;

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
            Sequence sequence = evidence is null ? DemoSequence.Create(seed) : ReferenceSequence.Create(evidence);
            if (trace is not null)
            {
                MotionTrace.Write(sequence, trace);
            }
            else if (capture is not null)
            {
                FrameCapture.Run(sequence, motionBlur, capture);
            }
            else if (benchmark is not null)
            {
                using var form = new BenchmarkForm(sequence, motionBlur, benchmark);
                Application.Run(form);
                return form.Completed ? 0 : 1;
            }
            else if (studio)
            {
                using var form = new StudioForm(sequence, motionBlur);
                Application.Run(form);
            }
            else
            {
                Rectangle screen = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Monitor primário indisponível.");
                OverlayLayout layout = OverlayLayout.Fit(screen.Size, region, feather);
                if (overlayBenchmark is not null)
                {
                    using var form = new BenchmarkForm(sequence, motionBlur, overlayBenchmark, layout);
                    Application.Run(form);
                    return form.Completed ? 0 : 1;
                }

                using var overlay = new OverlayWindow(sequence, motionBlur, loop, screen, layout);
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

    private static string Argument(string[] args, ref int index)
        => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"Falta o valor de {args[index]}.");
}
