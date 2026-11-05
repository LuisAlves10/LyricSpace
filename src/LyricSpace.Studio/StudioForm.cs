using System.Diagnostics;
using System.Globalization;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Playback;
using LyricSpace.Studio.Rendering;

namespace LyricSpace.Studio;

internal sealed class StudioForm : Form
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly double[] Speeds = [0.25, 0.5, 1, 1.5, 2];

    private readonly Sequence _sequence;
    private readonly StageSurface _surface = new() { Dock = DockStyle.Fill };
    private readonly TrackBar _seek = new()
    {
        Name = "Seek",
        AccessibleName = "Posição na sequência",
        Dock = DockStyle.Fill,
        TickStyle = TickStyle.None,
        SmallChange = 100,
        LargeChange = 500,
        AutoSize = false
    };
    private readonly Label _time = Label("Tempo", ContentAlignment.MiddleRight);
    private readonly Button _play = Button("&Pausar");
    private readonly Button _restart = Button("&Reiniciar");
    private readonly ComboBox _speed = new()
    {
        Name = "Speed",
        AccessibleName = "Velocidade",
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 83
    };
    private readonly CheckBox _motionBlur = new()
    {
        Text = "Motion blur",
        AutoSize = true,
        Margin = new Padding(16, 7, 0, 0)
    };
    private readonly Label _performance = Label("Desempenho", ContentAlignment.MiddleRight);
    private readonly PlaybackClock _clock;
    private readonly FramePacer _pacer = new();
    private readonly Action<GpuSample> _recordGpu;
    private StageRenderer? _renderer;
    private bool _dirty = true;
    private bool _resumeAfterSeek;
    private bool _draggingSeek;
    private bool _updatingSeek;
    private long _frame;
    private int _intervalFrames;
    private double _statsAnchor;
    private double _cpuTotal;
    private double? _gpuMs;

    public StudioForm(Sequence sequence, bool motionBlur)
    {
        _sequence = sequence;
        _clock = new PlaybackClock(sequence.Duration);
        _recordGpu = sample => _gpuMs = sample.Milliseconds;

        Text = "LyricSpace Studio";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1220, 770);
        MinimumSize = new Size(820, 442);
        BackColor = Color.FromArgb(26, 27, 34);
        ForeColor = Color.FromArgb(238, 235, 230);
        Font = new Font("Segoe UI", 10);

        _seek.Maximum = (int)Math.Round(sequence.Duration * 1000);
        _motionBlur.Checked = motionBlur;
        _speed.Items.AddRange(Speeds.Select(speed => speed.ToString("0.##", Portuguese) + "×").ToArray());
        _speed.SelectedIndex = 2;

        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 83,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(12, 6, 12, 5)
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        controls.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        controls.Controls.Add(_seek, 0, 0);
        controls.Controls.Add(_time, 1, 0);
        var transport = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty
        };
        transport.Controls.AddRange([_play, _restart, _speed, _motionBlur]);
        var status = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        status.Controls.Add(_performance);
        status.Controls.Add(transport);
        controls.Controls.Add(status, 0, 1);
        controls.SetColumnSpan(status, 2);
        Controls.Add(_surface);
        Controls.Add(controls);

        _play.Click += (_, _) => TogglePlayback();
        _restart.Click += (_, _) =>
        {
            double now = FramePacer.Now;
            _clock.Seek(0, now);
            _clock.Play(now);
            ResetMetrics(now);
            UpdateControls(0);
            _dirty = true;
        };
        _speed.SelectedIndexChanged += (_, _) => _clock.SetSpeed(Speeds[_speed.SelectedIndex], FramePacer.Now);
        _seek.ValueChanged += (_, _) =>
        {
            if (_updatingSeek)
            {
                return;
            }

            double position = _seek.Value / 1000.0;
            _clock.Seek(position, FramePacer.Now);
            UpdateControls(position);
            _dirty = true;
        };
        _seek.MouseDown += (_, _) =>
        {
            _draggingSeek = true;
            _resumeAfterSeek = _clock.IsPlaying;
            _clock.Pause(FramePacer.Now);
        };
        _seek.MouseUp += (_, _) => EndSeek();
        _seek.MouseCaptureChanged += (_, _) =>
        {
            if (_draggingSeek && !_seek.Capture)
            {
                EndSeek();
            }
        };
        _motionBlur.CheckedChanged += (_, _) => _dirty = true;
        _surface.SizeChanged += (_, _) =>
        {
            _dirty = true;
            ResetMetrics(FramePacer.Now);
        };
        _surface.Paint += (_, _) => _dirty = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _renderer = new StageRenderer(_sequence, _surface.Handle, _surface.Width, _surface.Height);
        double now = FramePacer.Now;
        _clock.Play(now);
        ResetMetrics(now);
        Application.Idle += OnIdle;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.Idle -= OnIdle;
            _renderer?.Dispose();
            _pacer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void EndSeek()
    {
        if (!_draggingSeek)
        {
            return;
        }

        _draggingSeek = false;
        double now = FramePacer.Now;
        if (_resumeAfterSeek && _clock.PositionAt(now) < _sequence.Duration)
        {
            _clock.Play(now);
            ResetMetrics(now);
        }

        UpdateControls(_clock.PositionAt(now));
        _dirty = true;
    }

    private void TogglePlayback()
    {
        double now = FramePacer.Now;
        if (_clock.IsPlaying)
        {
            _clock.Pause(now);
        }
        else
        {
            _clock.Play(now);
            ResetMetrics(now);
        }

        UpdateControls(_clock.PositionAt(now));
        _dirty = true;
    }

    private void ResetMetrics(double now)
    {
        _statsAnchor = now;
        _intervalFrames = 0;
        _cpuTotal = 0;
        _pacer.Reset(now);
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        while (!FramePacer.HasMessages && !IsDisposed && WindowState != FormWindowState.Minimized
            && _surface.Width > 0 && _surface.Height > 0 && (_clock.IsPlaying || _dirty))
        {
            double now = FramePacer.Now;
            if (!_dirty && !_pacer.IsDueOrWait(now))
            {
                continue;
            }

            Render(now);
            _pacer.Advance(now);
        }
    }

    private void Render(double now)
    {
        StageRenderer renderer = _renderer!;
        renderer.Resize(_surface.Width, _surface.Height);
        double time = _clock.PositionAt(now);
        if (_clock.IsPlaying && time >= _sequence.Duration)
        {
            _clock.Pause(now);
        }

        _dirty = false;
        long start = Stopwatch.GetTimestamp();
        renderer.Draw(time, _motionBlur.Checked, _frame++, _recordGpu);
        _cpuTotal += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        bool presented = renderer.Present();
        if (presented)
        {
            _intervalFrames++;
        }

        if (now - _statsAnchor >= 0.5 || !_clock.IsPlaying)
        {
            UpdateControls(time);
            UpdatePerformance(now);
        }
    }

    private void UpdatePerformance(double now)
    {
        double interval = now - _statsAnchor;
        if (_clock.IsPlaying && _intervalFrames > 1 && interval > 0)
        {
            double fps = _intervalFrames / interval;
            string gpu = _gpuMs is double ms ? ms.ToString("0.0", Portuguese) : "—";
            _performance.Text = string.Format(Portuguese, "{0:0} FPS  ·  {1:0.0} ms  ·  CPU {2:0.0} / GPU {3} ms",
                fps, 1000 / fps, _cpuTotal / _intervalFrames, gpu);
        }
        else if (!_clock.IsPlaying)
        {
            _performance.Text = "Em pausa";
        }

        _intervalFrames = 0;
        _cpuTotal = 0;
        _statsAnchor = now;
    }

    private void UpdateControls(double time)
    {
        int value = Math.Clamp((int)Math.Round(time * 1000), 0, _seek.Maximum);
        if (!_draggingSeek)
        {
            _updatingSeek = true;
            try
            {
                _seek.Value = value;
            }
            finally
            {
                _updatingSeek = false;
            }
        }
        _time.Text = string.Format(Portuguese, "{0:0.00} / {1:0.00} s", time, _sequence.Duration);
        _play.Text = _clock.IsPlaying ? "&Pausar" : "&Reproduzir";
    }

    private static Button Button(string text) => new()
    {
        Text = text,
        Width = 108,
        Height = 31,
        Margin = new Padding(0, 2, 7, 0),
        BackColor = Color.FromArgb(51, 52, 63),
        ForeColor = Color.FromArgb(246, 243, 238),
        FlatStyle = FlatStyle.Flat
    };

    private static Label Label(string accessibleName, ContentAlignment align) => new()
    {
        AccessibleName = accessibleName,
        Dock = DockStyle.Fill,
        TextAlign = align,
        ForeColor = Color.FromArgb(223, 220, 220)
    };
}

internal sealed class StageSurface : Control
{
    public StageSurface()
    {
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        TabStop = false;
        AccessibleName = "Composição tipográfica 3D";
        BackColor = Color.FromArgb(18, 18, 22);
    }
}
