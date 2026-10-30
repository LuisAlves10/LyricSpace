using System.Numerics;

namespace Lenoz.Motion;

public static class PerspectiveCamera
{
    public static Matrix4x4 ViewProjection(Matrix4x4 world, float verticalFovRadians, float aspect, float nearPlane, float farPlane)
    {
        if (!Matrix4x4.Invert(world, out Matrix4x4 view))
        {
            throw new ArgumentException("The camera world transform must be invertible.", nameof(world));
        }

        return view * Matrix4x4.CreatePerspectiveFieldOfView(verticalFovRadians, aspect, nearPlane, farPlane);
    }

    public static float VerticalFovFromZoom(float height, float zoom)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoom);
        return 2 * MathF.Atan(height / (2 * zoom));
    }
}
