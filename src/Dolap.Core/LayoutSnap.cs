namespace Dolap.Core;

public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>Desktop coordinates; caller supplies DPI-scaled distances.</summary>
public static class LayoutSnap
{
    public static LayoutRect Apply(LayoutRect moving, LayoutRect screen, IEnumerable<LayoutRect> neighbors,
        double threshold = 18, double gap = 12)
    {
        var dx = double.PositiveInfinity; var dy = double.PositiveInfinity;
        void X(double c) { if (Math.Abs(c) <= threshold && Math.Abs(c) < Math.Abs(dx)) dx = c; }
        void Y(double c) { if (Math.Abs(c) <= threshold && Math.Abs(c) < Math.Abs(dy)) dy = c; }
        X(screen.X - moving.X); X(screen.Right - moving.Right);
        Y(screen.Y - moving.Y); Y(screen.Bottom - moving.Bottom);
        foreach (var n in neighbors)
        {
            if (moving.Bottom >= n.Y - threshold && moving.Y <= n.Bottom + threshold)
            {
                X(n.Right + gap - moving.X); X(n.X - gap - moving.Right);
                X(n.X - moving.X); X(n.Right - moving.Right);
            }
            if (moving.Right >= n.X - gap - threshold && moving.X <= n.Right + gap + threshold)
            {
                Y(n.Bottom + gap - moving.Y); Y(n.Y - gap - moving.Bottom);
                Y(n.Y - moving.Y); Y(n.Bottom - moving.Bottom);
            }
        }
        return moving with { X = moving.X + (double.IsFinite(dx) ? dx : 0), Y = moving.Y + (double.IsFinite(dy) ? dy : 0) };
    }
}
