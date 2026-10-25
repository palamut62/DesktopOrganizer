using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Dolap.App;

/// <summary>Duvara asili TV gorunumlu canli sistem monitoru. Yalniz gorunurken 2 sn'de bir olcer (gizliyken is yapmaz).</summary>
public partial class MonitorWindow : Window
{
    readonly SystemStats _stats = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly List<double> _history = [];
    bool _busy;
    IntPtr _hwnd;

    /// <summary>Kullanici surukleyip birakti: (calisma alanina gore 0..1 x, y, monitor adi).</summary>
    public event Action<double, double, string>? Moved;

    public MonitorWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshStats();
        IsVisibleChanged += async (_, _) => { if (IsVisible) { _timer.Start(); await RefreshStats(); } else _timer.Stop(); };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
        };
        Closed += (_, _) => { _timer.Stop(); _stats.Dispose(); };
        // Ilk gosterimde WPF varsayilan konumu uygular; kayitli konum ondan sonra verilir.
        ContentRendered += (_, _) => ApplyPlace();
    }

    /// <summary>Kaydedilmis konuma yerlestirir (fiziksel piksel; cok monitor/DPI icin SetWindowPos).</summary>
    (double X, double Y, string? Device) _place = (0.5, 0.04, null);

    public void Place(double fx, double fy, string? device)
    {
        _place = (fx, fy, device);
        if (IsLoaded) ApplyPlace();
    }

    void ApplyPlace()
    {
        var (fx, fy, device) = _place;
        if (_hwnd == IntPtr.Zero) _hwnd = new WindowInteropHelper(this).EnsureHandle();
        var sc = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device) ?? Forms.Screen.PrimaryScreen!;
        var wa = sc.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        UpdateLayout();
        int w = (int)(ActualWidth * dpi), h = (int)(ActualHeight * dpi);
        int x = wa.Left + (int)(Math.Clamp(fx, 0, 1) * Math.Max(0, wa.Width - w));
        int y = wa.Top + (int)(Math.Clamp(fy, 0, 1) * Math.Max(0, wa.Height - h));
        Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    /// <summary>Boyut ve cerceve temasi. Ekran icerigi ve olcum renkleri ayni kalir (okunurluk).</summary>
    public void ApplyStyle(string theme, double scale)
    {
        RootScale.ScaleX = RootScale.ScaleY = Math.Clamp(scale, 0.6, 1.6);
        static Brush V(params string[] c)
        {
            var b = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
            for (var i = 0; i < c.Length; i++) b.GradientStops.Add(new((Color)ColorConverter.ConvertFromString(c[i]), i / (double)(c.Length - 1)));
            b.Freeze(); return b;
        }
        static Brush S(string c) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
        (Bezel.Background, Bezel.BorderBrush, Led.Fill) = theme switch
        {
            "wood" => (V("#FF7A4F2E", "#FF5A361C", "#FF3B220F"), S("#FF26170A"), S("#FFF59E0B")),
            "silver" => (V("#FFD9DDE2", "#FFA3AAB2", "#FF6E757D"), S("#FF555C64"), S("#FF3BE07A")),
            "neon" => (V("#FF14101F", "#FF0B0912", "#FF07060C"), S("#FFA855F7"), S("#FFE879F9")),
            _ => (V("#FF24272C", "#FF111315", "#FF0A0B0D"), S("#FF34383F"), S("#FF3BE07A")),
        };
        Bezel.BorderThickness = new(theme == "neon" ? 2 : 1.5);
        Bezel.Effect = theme == "neon" ? new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(168, 85, 247), BlurRadius = 18, ShadowDepth = 0, Opacity = .6 } : null;
        if (IsLoaded) Dispatcher.BeginInvoke(ApplyPlace, DispatcherPriority.Loaded);
    }

    void Bezel_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        Native.GetWindowRect(_hwnd, out var r0);
        try { DragMove(); } catch (InvalidOperationException) { }
        Native.GetWindowRect(_hwnd, out var r);
        if (r.Left == r0.Left && r.Top == r0.Top) return;
        var sc = Forms.Screen.FromRectangle(new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
        var wa = sc.WorkingArea; int w = r.Right - r.Left, h = r.Bottom - r.Top;
        Moved?.Invoke(wa.Width - w <= 0 ? 0 : (double)(r.Left - wa.Left) / (wa.Width - w),
                      wa.Height - h <= 0 ? 0 : (double)(r.Top - wa.Top) / (wa.Height - h), sc.DeviceName);
    }

    async Task RefreshStats()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var s = await Task.Run(_stats.Sample);
            Clock.Text = DateTime.Now.ToString("HH:mm");
            Show(CpuText, CpuBar, s.Cpu);
            if (s.Gpu is { } g) Show(GpuText, GpuBar, g); else { GpuText.Text = "yok"; Scale(GpuBar, 0); }
            var ramPct = s.RamTotalGb <= 0 ? 0 : s.RamUsedGb / s.RamTotalGb * 100;
            Show(RamText, RamBar, ramPct);
            RamSub.Text = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} GB";
            Drives.ItemsSource = s.Drives.Take(3).Select(d => new { d.Name, Label = $"%{d.Percent:0}", Fraction = d.Percent / 100 }).ToList();
            Procs.ItemsSource = s.Top.Select(p => new { p.Name, Cpu = $"%{p.Cpu:0.0}", Ram = $"{p.RamMb:0} MB" }).ToList();
            _history.Add(s.Cpu);
            if (_history.Count > 30) _history.RemoveAt(0); // 30 x 2 sn = son 1 dakika
            DrawChart();
        }
        finally { _busy = false; }
    }

    static void Show(System.Windows.Controls.TextBlock t, FrameworkElement bar, double pct)
    {
        t.Text = $"%{pct:0}";
        Scale(bar, pct / 100);
    }

    static void Scale(FrameworkElement bar, double f) =>
        bar.RenderTransform = new ScaleTransform(Math.Clamp(f, 0, 1), 1);

    void DrawChart()
    {
        double w = Chart.ActualWidth, h = Chart.ActualHeight;
        if (w <= 0 || h <= 0 || _history.Count < 2) return;
        var step = w / 29;
        var x0 = w - (_history.Count - 1) * step;
        var line = new PointCollection(_history.Select((v, i) => new Point(x0 + i * step, h - v / 100 * (h - 2))));
        CpuLine.Points = line;
        var fill = new PointCollection(line) { new Point(w, h), new Point(x0, h) };
        CpuFill.Points = fill;
    }
}
