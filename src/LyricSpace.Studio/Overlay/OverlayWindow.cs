using System.ComponentModel;
using System.Runtime.InteropServices;
using LyricSpace.Studio.Composition;
using LyricSpace.Studio.Playback;
using LyricSpace.Studio.Rendering;

namespace LyricSpace.Studio.Overlay;

internal sealed class OverlayWindow : Form
{
    private readonly Sequence _sequence;
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

    public OverlayWindow(Sequence sequence, bool motionBlur, bool loop, Rectangle display, OverlayLayout layout)
    {
        _sequence = sequence;
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
            }

            Rectangle region = _editor!.ReadDrag();
            if (region != _layout.Region)
            {
                _layout = _layout.At(region);
            }

            _renderer!.UpdateOverlayLayout(_layout, editing: true);
        }
        else if (_editingSince is double started)
        {
            _editor!.End();
            _start += now - started;
            _editingSince = null;
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
