using System.Globalization;

namespace LyricSpace.Studio.Overlay;

internal readonly record struct OverlayLayout(Rectangle Region, float Feather)
{
    public OverlayLayout At(Rectangle rectangle)
    {
        float previous = Math.Min(Region.Width, Region.Height);
        float next = Math.Min(rectangle.Width, rectangle.Height);
        return new OverlayLayout(rectangle, Feather * (next / previous));
    }

    public static OverlayLayout Fit(Size output, string? region, float? feather)
    {
        Rectangle rectangle = region is null ? OverlaySettings.Load(output) ?? OverlayPlacement.Default(output) : default;
        if (region is not null)
        {
            string[] parts = region.Split(',');
            if (parts.Length != 4)
            {
                throw new ArgumentException("Use --region X,Y,LARGURA,ALTURA em pixels.");
            }

            int[] values = parts.Select(part => int.Parse(part, NumberStyles.Integer, CultureInfo.InvariantCulture)).ToArray();
            rectangle = new Rectangle(values[0], values[1], values[2], values[3]);
            if (!OverlayPlacement.Fits(rectangle, output))
            {
                throw new ArgumentException("A região precisa caber no monitor primário.");
            }
        }

        float maximum = Math.Min(rectangle.Width, rectangle.Height) / 2f;
        float softEdge = feather ?? Math.Min(96, maximum * 0.20f);
        if (!(softEdge >= 0 && softEdge <= maximum))
        {
            throw new ArgumentException("O feather deve caber em metade do menor lado da região.");
        }

        return new OverlayLayout(rectangle, softEdge);
    }
}
