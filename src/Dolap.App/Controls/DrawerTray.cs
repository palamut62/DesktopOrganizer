using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Dolap.App.Controls;

/// <summary>Cekmece ic tepsisi: Progress (0..1) kadar yukseklik gosterir, SlideX tum cekmeceyi yana kaydirir.
/// Hedef durum degisince animasyon mevcut konumdan yon degistirir; kuyruk birikmez.</summary>
public sealed class DrawerTray : Decorator
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(DrawerTray),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, e) => ((DrawerTray)d).UpdateSlide()));
    public static readonly DependencyProperty SlideXProperty = DependencyProperty.Register(nameof(SlideX), typeof(double), typeof(DrawerTray));
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(DrawerTray),
        new PropertyMetadata(false, (d, e) => ((DrawerTray)d).Animate((bool)e.NewValue)));
    public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(nameof(Direction), typeof(double), typeof(DrawerTray),
        new PropertyMetadata(-1.0, (d, e) => ((DrawerTray)d).UpdateSlide()));

    public const double SlideDistance = 84;
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public double SlideX { get => (double)GetValue(SlideXProperty); set => SetValue(SlideXProperty, value); }
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public double Direction { get => (double)GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }

    void UpdateSlide() => SetValue(SlideXProperty, Progress * SlideDistance * Direction);

    void Animate(bool open)
    {
        var res = Application.Current.Resources;
        var ms = (double)res[open ? "OpenMs" : "CloseMs"];
        if ((bool)res["ReduceMotion"])
        {
            BeginAnimation(ProgressProperty, null);
            Progress = open ? 1 : 0;
            return;
        }
        // From verilmez: animasyon mevcut Progress degerinden baslar (hizli tiklamada yon degisir).
        BeginAnimation(ProgressProperty, new DoubleAnimation(open ? 1 : 0, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = (IEasingFunction)res[open ? "EaseOpen" : "EaseClose"],
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    protected override Size MeasureOverride(Size c)
    {
        if (Child == null) return default;
        Child.Measure(new Size(c.Width, double.PositiveInfinity));
        return new Size(Child.DesiredSize.Width, Child.DesiredSize.Height * Progress);
    }

    protected override Size ArrangeOverride(Size f)
    {
        // Alt hizali: tepsi cekmece onunden yukari dogru acilir.
        Child?.Arrange(new Rect(0, f.Height - Child.DesiredSize.Height, f.Width, Child.DesiredSize.Height));
        return f;
    }

    protected override Geometry? GetLayoutClip(Size layoutSlotSize) => new RectangleGeometry(new Rect(RenderSize));
}
