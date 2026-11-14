using System.Diagnostics;
using System.Text.Json;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Overlay;
using LyricSpace.Studio.Playback;
using LyricSpace.Studio.Rendering;

namespace LyricSpace.Studio.Diagnostics;

internal sealed class BenchmarkForm : Form
{
    private const double WarmupSeconds = 2;
    private const double MeasurementSeconds = 20;

    private readonly Sequence _sequence;
    private readonly OverlayLayout? _overlay;
    private readonly bool _motionBlur;
    private readonly string _output;
    private readonly StageSurface _surface = new() { Dock = DockStyle.Fill };
    private readonly FramePacer _pacer = new();
    private readonly Process _process = Process.GetCurrentProcess();

    private readonly List<double> _intervals = new(1440);
    private readonly List<double> _cpuTimes = new(1440);
    private readonly List<double> _gpuTimes = new(1440);
    private readonly List<double> _presentTimes = new(1440);
    private readonly Action<GpuSample> _recordGpu;
    private StageRenderer? _renderer;
    private double _started;
    private double _measureStart;
    private double _lastSubmit;
    private double _cpuAtStart;
    private long _firstMeasuredFrame = long.MaxValue;
    private long _frame;
    private uint? _firstPresentCount;

    public BenchmarkForm(Sequence sequence, bool motionBlur, string output, OverlayLayout? overlay = null)
    {
        _sequence = sequence;
        _overlay = overlay;
        _motionBlur = motionBlur;
        _output = Path.GetFullPath(output);
        _recordGpu = sample =>
        {
            if (sample.Frame >= _firstMeasuredFrame)
            {
                _gpuTimes.Add(sample.Milliseconds);
            }
        };

        Text = "LyricSpace Studio — benchmark";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        if (overlay is not null)
        {
            Text = "LyricSpace Overlay";
            TopMost = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            return;
        }

        KeyPreview = true;
        Controls.Add(_surface);
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    public bool Completed { get; private set; }
    protected override bool ShowWithoutActivation => _overlay is not null;
    protected override CreateParams CreateParams => _overlay is not null
        ? InputTransparentWindow.Configure(base.CreateParams)
        : base.CreateParams;

    protected override void WndProc(ref Message message)
    {
        if (_overlay is null || !InputTransparentWindow.Handle(ref message))
        {
            base.WndProc(ref message);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_overlay is not null)
        {
            _renderer = new StageRenderer(_sequence, Handle, Width, Height, _overlay);
            InputTransparentWindow.Pin(Handle);
        }
        else
        {
            _renderer = new StageRenderer(_sequence, _surface.Handle, _surface.Width, _surface.Height);
        }
        _started = FramePacer.Now;
        _pacer.Reset(_started);
        Application.Idle += OnIdle;
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            throw new InvalidOperationException("A janela do benchmark precisa permanecer visível.");
        }

        while (!FramePacer.HasMessages && !IsDisposed)
        {
            double now = FramePacer.Now;
            if (!_pacer.IsDueOrWait(now))
            {
                continue;
            }

            StageRenderer renderer = _renderer!;
            double age = now - _started;
            if (_measureStart == 0 && age >= WarmupSeconds)
            {
                _measureStart = now;
                _firstMeasuredFrame = _frame;
                _firstPresentCount = renderer.PresentedCount;
                _cpuAtStart = _process.TotalProcessorTime.TotalSeconds;
            }

            if (_measureStart > 0 && now - _measureStart >= MeasurementSeconds)
            {
                Finish(renderer, now - _measureStart);
                Close();
                return;
            }

            long start = Stopwatch.GetTimestamp();
            renderer.Draw(age % _sequence.Duration, _motionBlur, _frame++, _recordGpu);
            double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            if (!renderer.Present())
            {
                throw new InvalidOperationException("O benchmark perdeu a superfície de apresentação.");
            }

            double presentMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _pacer.Advance(now);
            if (_measureStart > 0)
            {
                if (_lastSubmit != 0)
                {
                    _intervals.Add((now - _lastSubmit) * 1000);
                }

                _lastSubmit = now;
                _cpuTimes.Add(cpuMs);
                _presentTimes.Add(presentMs);
            }
        }
    }

    private void Finish(StageRenderer renderer, double seconds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        uint? finalPresentCount = renderer.PresentedCount;
        double? displayedFps = _firstPresentCount is uint first && finalPresentCount is uint last && last >= first
            ? (last - first) / seconds : null;
        var results = new
        {
            Adapter = renderer.AdapterName,
            renderer.Width,
            renderer.Height,
            Overlay = _overlay is not null,
            MotionBlur = _motionBlur,
            MeasurementSeconds = seconds,
            Submitted = _cpuTimes.Count,
            SubmittedFps = _cpuTimes.Count / seconds,
            DxgiPresentFps = displayedFps,
            FrameIntervalMs = Stats(_intervals),
            CpuSubmitMs = Stats(_cpuTimes),
            GpuRenderMs = Stats(_gpuTimes),
            PresentCallMs = Stats(_presentTimes),
            CpuPercentOfOneLogicalCore = (_process.TotalProcessorTime.TotalSeconds - _cpuAtStart) / seconds * 100,
            WorkingSetMiB = _process.WorkingSet64 / (1024.0 * 1024),
            DxgiLocalUsageMiB = renderer.LocalVideoMemory / (1024.0 * 1024),
            GlyphTextureMiB = renderer.GlyphBytes / (1024.0 * 1024)
        };
        File.WriteAllText(_output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Completed = true;
    }

    private static object? Stats(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        return new
        {
            Count = values.Count,
            Mean = values.Average(),
            P95 = values[(int)Math.Ceiling(values.Count * 0.95) - 1],
            Max = values[^1]
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.Idle -= OnIdle;
            _renderer?.Dispose();
            _pacer.Dispose();
            _process.Dispose();
        }

        base.Dispose(disposing);
    }
}
