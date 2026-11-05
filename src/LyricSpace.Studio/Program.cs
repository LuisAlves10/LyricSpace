using System.Globalization;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Diagnostics;

namespace LyricSpace.Studio;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool hasDiagnosticOutput = args.Contains("--capture") || args.Contains("--benchmark");
        try
        {
            uint seed = DemoSequence.DefaultSeed;
            bool motionBlur = true;
            string? capture = null;
            string? benchmark = null;
            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--seed":
                        seed = uint.Parse(Argument(args, ref index), CultureInfo.InvariantCulture);
                        break;
                    case "--capture":
                        capture = Argument(args, ref index);
                        break;
                    case "--benchmark":
                        benchmark = Argument(args, ref index);
                        break;
                    case "--no-blur":
                        motionBlur = false;
                        break;
                    default:
                        throw new ArgumentException($"Opção desconhecida: {args[index]}");
                }
            }

            if (capture is not null && benchmark is not null)
            {
                throw new ArgumentException("Escolha uma operação de diagnóstico por execução.");
            }

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Sequence sequence = DemoSequence.Create(seed);
            if (capture is not null)
            {
                FrameCapture.Run(sequence, motionBlur, capture);
            }
            else if (benchmark is not null)
            {
                using var form = new BenchmarkForm(sequence, motionBlur, benchmark);
                Application.Run(form);
                return form.Completed ? 0 : 1;
            }
            else
            {
                using var form = new StudioForm(sequence, motionBlur);
                Application.Run(form);
            }

            return 0;
        }
        catch (Exception error)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LyricSpace");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "studio-error.log"), error.ToString());
            if (hasDiagnosticOutput)
            {
                Console.Error.WriteLine(error);
            }
            else
            {
                MessageBox.Show(error.Message, "LyricSpace Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return 1;
        }
    }

    private static string Argument(string[] args, ref int index)
        => index + 1 < args.Length ? args[++index] : throw new ArgumentException($"Falta o valor de {args[index]}.");
}
