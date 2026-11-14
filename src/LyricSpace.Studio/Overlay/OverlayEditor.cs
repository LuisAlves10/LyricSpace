using System.Runtime.InteropServices;

namespace LyricSpace.Studio.Overlay;

/// <summary>Mouse surface limited to the composition region, visible only during the edit chord.</summary>
internal sealed partial class OverlayEditor : Form
{
    private const int Grip = 20;
    private readonly Rectangle _display;
    private DragStart? _drag;
    private Rectangle _region;

    public OverlayEditor(Rectangle display)
    {
        _display = display;
        Text = "LyricSpace Layout";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.008;
    }

    public static bool ChordHeld => IsDown(0x11) && IsDown(0x10) && IsDown(0x12);
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x80; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    public void Begin(Rectangle region)
    {
        UpdateBounds(region);
        Show();
        InputTransparentWindow.PlaceAbove(Handle);
    }

    public void End()
    {
        _drag = null;
        Capture = false;
        Hide();
    }

    public Rectangle ReadDrag()
    {
        if (_drag is not { } start)
        {
            return _region;
        }

        int primaryMouseButton = SystemInformation.MouseButtonsSwapped ? 0x02 : 0x01;
        Point now = Cursor.Position;
        var delta = new Point(now.X - start.Cursor.X, now.Y - start.Cursor.Y);
        Rectangle rectangle = OverlayPlacement.Drag(start.Region, delta, start.Corner, _display.Size);
        if (!IsDown(primaryMouseButton))
        {
            _drag = null;
            Capture = false;
        }

        UpdateBounds(rectangle);
        return _region;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && ChordHeld)
        {
            Point mouse = Cursor.Position;
            _drag = new DragStart(_region, mouse, FindCorner(e.Location));
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point corner = FindCorner(e.Location);
        Cursor = corner.IsEmpty ? Cursors.SizeAll : corner.X == corner.Y ? Cursors.SizeNWSE : Cursors.SizeNESW;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021)
        {
            message.Result = 3; // MA_NOACTIVATE
            return;
        }

        base.WndProc(ref message);
    }

    private void UpdateBounds(Rectangle region)
    {
        _region = region;
        Bounds = new Rectangle(_display.Left + region.Left, _display.Top + region.Top, region.Width, region.Height);
    }

    private Point FindCorner(Point position) => OverlayPlacement.Corner(new Rectangle(Point.Empty, _region.Size), position, Grip);

    private static bool IsDown(int virtualKey) => GetAsyncKeyState(virtualKey) < 0;

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    private readonly record struct DragStart(Rectangle Region, Point Cursor, Point Corner);
}
