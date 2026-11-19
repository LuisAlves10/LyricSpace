using System.ComponentModel;
using System.Runtime.InteropServices;
using Lenoz.Motion;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Playback;
using LyricSpace.Studio.Rendering;

namespace LyricSpace.Studio.Overlay;

internal sealed class OverlayWindow : Form
{
    private static readonly Sequence EmptyScene = new(
        new Rig(new RigNode("camera", null, _ => new Pose(new System.Numerics.Vector3(0, 0, 9)))),
        0, new ScalarCurve(new Key(0, 1390)), new ScalarCurve(new Key(0, 0)), [], 1);
    private readonly Sequence _sequence;
    private readonly LivePlayback? _live;
    private readonly System.Windows.Forms.Timer? _wake;
    private readonly NotifyIcon? _tray;
    private readonly ContextMenuStrip? _trayMenu;
    private Sequence? _renderedScene;
    private double _lastTime = double.NaN;
    private bool _dirty;
    private readonly bool _motionBlur;
    private readonly bool _loop;
    private readonly Rectangle _display;
    private OverlayLayout _layout;
    private OverlayEditor? _editor;
    private double? _editingSince;
    private Rectangle _originalRegion;
    private readonly FramePacer _pacer = new();
    private StageRenderer? _renderer;
    private double _start;
    private long _frame;

    public OverlayWindow(Sequence sequence, bool motionBlur, bool loop, Rectangle display, OverlayLayout layout, LivePlayback? live)
    {
        _sequence = live is null ? sequence : EmptyScene;
        _live = live;
        if (live is not null)
        {
            _wake = new System.Windows.Forms.Timer { Interval = 40 };
            _wake.Tick += Render;
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Sair do LyricSpace", null, (_, _) => Close());
            _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "LyricSpace", ContextMenuStrip = _trayMenu };
        }
        _motionBlur = motionBlur;
        _loop = loop;
        _layout = layout;
        _display = display;
        Text = "LyricSpace Overlay";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = display;
        TopMost = true;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams => InputTransparentWindow.Configure(base.CreateParams);

    protected override void WndProc(ref Message message)
    {
        if (!InputTransparentWindow.Handle(ref message))
        {
            base.WndProc(ref message);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _renderer = new StageRenderer(_sequence, Handle, Width, Height, _layout);
        InputTransparentWindow.Pin(Handle);
        _start = FramePacer.Now;
        _pacer.Reset(_start);
        if (_tray is not null)
        {
            _tray.Visible = true;
        }

        Application.Idle += Render;
    }

    private void Render(object? sender, EventArgs e)
    {
        while (!IsDisposed && !FramePacer.HasMessages)
        {
            double now = FramePacer.Now;
            if (!_pacer.IsDueOrWait(now))
            {
                continue;
            }

            Edit(now);
            if (_live is not null)
            {
                if (!RenderLive(now))
                {
                    return;
                }

                _pacer.Advance(now);
                continue;
            }

            double time = (_editingSince ?? now) - _start;
            if (!_loop && time > _sequence.Duration)
            {
                Close();
                return;
            }

            _renderer!.Draw(_loop ? time % _sequence.Duration : time, _motionBlur, _frame++);
            _renderer.Present();
            _pacer.Advance(now);
        }
    }

    private bool RenderLive(double now)
    {
        LiveFrame? playback = _live!.Frame;
        double time = playback?.Clock.At(now) ?? 0;
        ProceduralSong? song = playback?.Song;
        Sequence scene = song?.CanRender(time) == true ? song.SceneAt(time)! : EmptyScene;
        if (scene == EmptyScene)
        {
            time = 0;
        }

        string status = _live.TrayStatus;
        if (_tray!.Text != status)
        {
            _tray.Text = status;
        }

        if (!ReferenceEquals(_renderedScene, scene) || time != _lastTime || _dirty)
        {
            _renderer!.SetSequence(scene);
            _renderer.Draw(time, _motionBlur, _frame++);
            _renderer.Present();
            _renderedScene = scene;
            _lastTime = time;
            _dirty = false;
        }

        bool moving = _editingSince is not null || (scene != EmptyScene && playback?.Clock.Rate > 0 && time < playback.Clock.Duration);
        _wake!.Enabled = !moving;
        return moving;
    }

    private void Edit(double now)
    {
        if (OverlayEditor.ChordHeld)
        {
            if (_editingSince is null)
            {
                _editor ??= new OverlayEditor(_display);
                _originalRegion = _layout.Region;
                _editor.Begin(_originalRegion);
                _editingSince = now;
                _dirty = true;
            }

            Rectangle region = _editor!.ReadDrag();
            if (region != _layout.Region)
            {
                _layout = _layout.At(region);
                _dirty = true;
            }

            _renderer!.UpdateOverlayLayout(_layout, editing: true);
        }
        else if (_editingSince is double started)
        {
            _editor!.End();
            _start += now - started;
            _editingSince = null;
            _dirty = true;
            _renderer!.UpdateOverlayLayout(_layout, editing: false);
            if (_layout.Region != _originalRegion)
            {
                OverlaySettings.Save(_display.Size, _layout.Region);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.Idle -= Render;
            _wake?.Dispose();
            _tray?.Dispose();
            _trayMenu?.Dispose();
            _editor?.Dispose();
            _renderer?.Dispose();
            _pacer.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal static partial class InputTransparentWindow
{
    public static CreateParams Configure(CreateParams parameters)
    {
        const int noActivate = 0x08000000;
        const int noRedirectionBitmap = 0x00200000;
        const int transparent = 0x00000020;
        const int toolWindow = 0x00000080;
        const int topMost = 0x00000008;
        const int layered = 0x00080000;
        const int disabled = 0x08000000;
        parameters.ExStyle |= noActivate | noRedirectionBitmap | transparent | toolWindow | topMost | layered;
        parameters.Style |= disabled;
        return parameters;
    }

    public static void Pin(nint window)
    {
        const uint alphaAttribute = 0x2;
        if (!SetLayeredWindowAttributes(window, 0, 255, alphaAttribute))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        PlaceAbove(window);
    }

    internal static void PlaceAbove(nint window)
    {
        const uint noSize = 0x1;
        const uint noMove = 0x2;
        const uint noActivate = 0x10;
        const uint noOwnerZOrder = 0x200;
        if (!SetWindowPos(window, -1, 0, 0, 0, 0, noSize | noMove | noActivate | noOwnerZOrder))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    public static bool Handle(ref Message message)
    {
        if (message.Msg == 0x0084)
        {
            message.Result = -1; // HTTRANSPARENT
            return true;
        }

        if (message.Msg == 0x0021)
        {
            message.Result = 3; // MA_NOACTIVATE
            return true;
        }

        return false;
    }

}
