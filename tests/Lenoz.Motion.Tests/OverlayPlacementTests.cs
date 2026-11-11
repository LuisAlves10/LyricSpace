using System.Drawing;
using LyricSpace.Studio.Overlay;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lenoz.Motion.Tests;

[TestClass]
public sealed class OverlayPlacementTests
{
    private static readonly Size Monitor = new(1920, 1080);

    [TestMethod]
    public void DefaultUsesACompactBottomRightRegion()
    {
        Rectangle region = OverlayPlacement.Default(Monitor);
        Assert.AreEqual(new Rectangle(1072, 585, 806, 453), region);
        Assert.IsTrue(OverlayPlacement.Fits(region, Monitor));
    }

    [TestMethod]
    public void CenterDragStaysWithinMonitorWithoutResizing()
    {
        var region = OverlayPlacement.Default(Monitor);
        var moved = OverlayPlacement.Drag(region, new Point(-8000, 200), Point.Empty, Monitor);
        Assert.AreEqual(region.Size, moved.Size);
        Assert.AreEqual(0, moved.Left);
        Assert.AreEqual(Monitor.Height, moved.Bottom);
    }

    [TestMethod]
    [DataRow(-1, -1)]
    [DataRow(1, -1)]
    [DataRow(-1, 1)]
    [DataRow(1, 1)]
    public void CornerDragPreservesAspectAndOppositeCorner(int x, int y)
    {
        var region = new Rectangle(600, 360, 720, 405);
        var moved = OverlayPlacement.Drag(region, new Point(160 * x, 90 * y), new Point(x, y), Monitor);
        Assert.AreEqual(880, moved.Width);
        Assert.AreEqual(495, moved.Height);
        Assert.AreEqual(x < 0 ? region.Right : region.Left, x < 0 ? moved.Right : moved.Left);
        Assert.AreEqual(y < 0 ? region.Bottom : region.Top, y < 0 ? moved.Bottom : moved.Top);
        Assert.IsTrue(OverlayPlacement.Fits(moved, Monitor));
    }

    [TestMethod]
    public void ResizeCannotEscapeMonitorOrCollapse()
    {
        var region = OverlayPlacement.Default(Monitor);
        var corner = new Point(-1, -1);
        var expanded = OverlayPlacement.Drag(region, new Point(-8000, -8000), corner, Monitor);
        var shrunk = OverlayPlacement.Drag(region, new Point(8000, 8000), corner, Monitor);
        Assert.AreEqual(region.Right, expanded.Right);
        Assert.AreEqual(region.Bottom, expanded.Bottom);
        Assert.IsTrue(OverlayPlacement.Fits(expanded, Monitor));
        Assert.AreEqual(new Size(240, 135), shrunk.Size);
        Assert.AreEqual(corner, OverlayPlacement.Corner(region, region.Location, 20));
        Assert.AreEqual(Point.Empty, OverlayPlacement.Corner(region, new Point(region.Left + 50, region.Top + 50), 20));
    }
}
