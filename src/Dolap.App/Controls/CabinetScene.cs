using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace Dolap.App.Controls;

/// <summary>Physical cabinet with a fixed carcass and independently translated drawers.
/// Interactive WPF controls are hosted on the actual 3D surfaces, not screenshot hotspots.</summary>
public sealed class CabinetScene : Grid
{
    /// <summary>Dunya biriminden DIP'e sabit olcek: kadraj donus icin genisleyince dolap kuculmez, pencere buyur.</summary>
    public const double PixelsPerUnit = 1.2;
    const double Pitch = 124, FrontWidth = 286;
    /// <summary>Cekmece cikis mesafesi; kutu derinliginden (168) kisa: arka duvar govde icinde kalir, cekmece kopmaz.</summary>
    public const double Travel = 150;
    readonly Viewport3D _view = new();
    readonly List<CategoryVm> _categories = [];
    readonly Dictionary<CategoryVm, DrawerPart> _drawers = [];
    bool _blocks, _reduceMotion, _lowQuality;
    /// <summary>Ses yalniz gercek uygulamada acilir; test/onizleme sessizdir.</summary>
    public bool Sounds { get; set; }
    bool _soundQueued, _soundOpen;
    int _side = 1;
    public event RoutedEventHandler? FrontClick, ItemClick, AddClick, SearchClick, SettingsClick, AddCategoryClick;
    public event ContextMenuEventHandler? FrontContext, ItemContext;
    public event DragEventHandler? DrawerEnter, DrawerLeave, DrawerDrop;
    public event MouseButtonEventHandler? HeaderDown;
    /// <summary>Kullanici fareyle dondurmeyi birakti: (yaw, pitch) derece; kaydedilmeli.</summary>
    public event Action<double, double>? ViewChanged;
    /// <summary>Kisayol baska bir kisayolun yerine birakildi (kaynak, hedef).</summary>
    public event Action<ShortcutVm, ShortcutVm>? ShortcutReordered;
    /// <summary>Kisayol bir cekmece onune birakildi: o cekmeceye tasinir.</summary>
    public event Action<ShortcutVm, CategoryVm>? ShortcutMoved;
    Button? _pressCard, _dropTarget; Point _pressPos; bool _reordering;
    double _yaw, _pitch, _cabinetHeight;
    public int DrawerCount => _drawers.Count;
    public IEnumerable<Button> ShortcutButtons => _drawers.Values.SelectMany(d => d.Cards.Select(c => c.Button));
    Button? _lifted;
    readonly Dictionary<Button, TranslateTransform3D> _lifts = [];
    const double LiftHeight = 34;
    public IEnumerable<Button> DrawerButtons => _frontButtons;
    /// <summary>Test icin: baslik yon oklari (sol, yukari, asagi, sag) ve gecerli/ hedef aci.</summary>
    internal List<RepeatButton> ArrowButtons { get; } = [];
    internal (double Yaw, double Pitch) ViewAngles => (_yaw, _pitch);
    internal (double Yaw, double Pitch) TargetAngles => (_targetYaw, _targetPitch);
    readonly List<Button> _frontButtons = [];

    sealed class DrawerPart
    {
        public TranslateTransform3D Transform { get; } = new();
        public required FrameworkElement Tray { get; init; }
        /// <summary>Cekmecede dik duran kisayol kartlari; her birinin kendi kaldirma donusumu var.</summary>
        public List<(Button Button, TranslateTransform3D Lift)> Cards { get; } = [];
    }

    public CabinetScene()
    {
        ClipToBounds = false;
        _view.ClipToBounds = false;
        // 3B dokular icin mipmap'li yuksek kaliteli filtre: kucultulmus dokuda titreme/bulaniklik azalir.
        RenderOptions.SetBitmapScalingMode(_view, BitmapScalingMode.HighQuality);
        Children.Add(_shadowLayer); // 3B sahnenin arkasinda: zemin golgesi
        Children.Add(_view);
        AutomationProperties.SetName(this, "Kategorili masaüstü dolabı");
        // Dondurme hareketleri: govdeyi (3B yuzey) sol tikla surukle, her yerde sag tikla surukle ya da Ctrl + sol surukle.
        _view.Cursor = Cursors.SizeAll;
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control && e.OriginalSource != _view) return;
            BeginRotate(e.GetPosition(this)); e.Handled = true;
        };
        PreviewMouseRightButtonDown += (_, e) => { _rightDown = e.GetPosition(this); _rightRotated = false; };
        PreviewMouseMove += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (_pressCard != null && !_reordering && e.LeftButton == MouseButtonState.Pressed && (p - _pressPos).Length > 8) StartReorder();
            if (_reordering) { UpdateDropTarget(); return; }
            if (_dragRotating) { RotateMove(p); return; }
            // Sag tik: 4 px'ten az hareket baglam menusu, fazlasi dondurme.
            if (_rightDown is { } d && e.RightButton == MouseButtonState.Pressed && (p - d).Length > 4)
            { _rightRotated = true; BeginRotate(d); RotateMove(p); }
        };
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_reordering) { EndReorder(); e.Handled = true; return; }
            _pressCard = null;
            if (_dragRotating && !_rightRotated) { EndRotate(); e.Handled = true; }
        };
        PreviewMouseRightButtonUp += (_, e) =>
        {
            if (_rightRotated) { EndRotate(); e.Handled = true; } // menu acilmasin
            _rightDown = null; _rightRotated = false;
        };
        LostMouseCapture += (_, _) => { if (_dragRotating) EndRotate(); };
    }

    public void SetCategories(IEnumerable<CategoryVm> categories)
    {
        foreach (var c in _categories) c.PropertyChanged -= CategoryChanged;
        _categories.Clear(); _categories.AddRange(categories);
        foreach (var c in _categories) c.PropertyChanged += CategoryChanged;
        Rebuild();
    }

    public void Configure(string theme, int side, bool reduceMotion, bool lowQuality)
    {
        var blocks = theme == "blocks";
        if (_built && blocks == _blocks && side == _side && reduceMotion == _reduceMotion && lowQuality == _lowQuality) return;
        _blocks = blocks; _side = side; _reduceMotion = reduceMotion; _lowQuality = lowQuality;
        Rebuild();
    }

    void CategoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CategoryVm.IsOpen) || sender is not CategoryVm c || !_drawers.TryGetValue(c, out var part)) return;
        var target = c.IsOpen ? Travel : 0;
        SetOpen(part, c.IsOpen);
        QueueSound(c.IsOpen);
        // Collapse tab stops immediately; the 3D drawer still closes smoothly.
        part.Tray.Focusable = false;
        Animate(part.Transform, TranslateTransform3D.OffsetZProperty, target);
    }

    void Animate(TranslateTransform3D transform, DependencyProperty property, double target)
    {
        if (_reduceMotion)
        {
            transform.BeginAnimation(property, null); transform.SetValue(property, target); return;
        }
        // Sample the presented value so reversal and rebuild never accumulate animation clocks.
        var current = (double)transform.GetValue(property);
        transform.BeginAnimation(property, null); transform.SetValue(property, target);
        transform.BeginAnimation(property, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(_blocks ? 290 : 340))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
    }

    public void FinishAnimations()
    {
        foreach (var (category, part) in _drawers)
        {
            part.Transform.BeginAnimation(TranslateTransform3D.OffsetZProperty, null);
            part.Transform.OffsetZ = category.IsOpen ? Travel : 0;
        }
    }

    public double DrawerTravel(CategoryVm c) => _drawers[c].Transform.OffsetZ;

    void Rebuild()
    {
        _built = true;
        _view.Children.Clear(); _drawers.Clear(); _frontButtons.Clear(); ArrowButtons.Clear(); _lifts.Clear(); _lifted = null;
        var count = Math.Max(1, _categories.Count);
        var cabinetHeight = count * Pitch + 86;
        _cabinetHeight = cabinetHeight;
        ComputeFit();
        UpdateCamera();
        var viewDirection = ViewDirection(_yaw, _pitch);
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(100, 104, 112)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(180, 175, 166), new(-.8, -1, -1.5)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(65, 88, 126), new(1, -.2, -.4)));
        _view.Children.Add(new ModelVisual3D { Content = lights });

        var body = CabinetMesh.Finish(_blocks ? "#153873" : "#4A5058", _blocks ? 90 : 65, .55,
            _blocks ? CabinetMesh.Plastic() : CabinetMesh.BrushedSteel(), tint: _blocks);
        var edges = CabinetMesh.Finish(_blocks ? "#102B59" : "#191D23", 80, .35);
        var chrome = CabinetMesh.Finish(_blocks ? "#22457C" : "#AEB8C4", 100, .95);
        var dark = CabinetMesh.Finish("#0D1118", 15, .05);
        AddFloorShadow();
        var carcass = new Model3DGroup();
        // Hollow carcass: the opening remains visible when a drawer is extracted.
        carcass.Children.Add(CabinetMesh.Box(-157, 18, -178, 14, cabinetHeight - 18, 190, body, 3));
        carcass.Children.Add(CabinetMesh.Box(143, 18, -178, 14, cabinetHeight - 18, 190, body, 3));
        carcass.Children.Add(CabinetMesh.Box(-143, 18, -180, 286, cabinetHeight - 18, 8, dark, 2));
        carcass.Children.Add(CabinetMesh.Box(-158, cabinetHeight - 10, -180, 316, 17, 198, body, 4));
        carcass.Children.Add(CabinetMesh.Box(-158, 14, -180, 316, 20, 198, edges, 4));
        carcass.Children.Add(CabinetMesh.Box(-138, 2, -145, 34, 14, 34, dark, 3));
        carcass.Children.Add(CabinetMesh.Box(104, 2, -145, 34, 14, 34, dark, 3));
        carcass.Children.Add(CabinetMesh.Box(-138, 2, -20, 34, 14, 34, dark, 3));
        carcass.Children.Add(CabinetMesh.Box(104, 2, -20, 34, 14, 34, dark, 3));
        // Top lip, base inset and front vertical seams catch the same scene lighting.
        carcass.Children.Add(CabinetMesh.Box(-157, cabinetHeight - 13, 11, 314, 3, 5, chrome, 1));
        carcass.Children.Add(CabinetMesh.Box(-154, 24, 12, 3, cabinetHeight - 42, 3, chrome, 1));
        carcass.Children.Add(CabinetMesh.Box(151, 24, 12, 3, cabinetHeight - 42, 3, chrome, 1));
        if (_blocks)
            foreach (var x in new[] { -105d, 0, 105 })
                foreach (var z in new[] { -130d, -45 })
                    carcass.Children.Add(CabinetMesh.Cylinder(new(x, cabinetHeight + 7, z), new(x, cabinetHeight + 21, z), 21, body));
        Add(carcass);
        Header(cabinetHeight, body);

        for (var i = 0; i < _categories.Count; i++)
        {
            var c = _categories[i]; var top = cabinetHeight - 54 - i * Pitch;
            var part = new DrawerPart { Tray = BuildTray(c) };
            _drawers.Add(c, part);
            part.Transform.OffsetZ = c.IsOpen ? Travel : 0;
            SetOpen(part, c.IsOpen);
            // Each drawer has a fixed dark bay; siblings never move when this drawer opens.
            var bay = new Model3DGroup();
            bay.Children.Add(CabinetMesh.Box(-143, top - 124, -170, 286, 4, 182, edges, 1));
            Add(bay);
            var moving = new Model3DGroup();
            var finish = CabinetMesh.Finish(_blocks ? c.Model.Color : "#59616A", _blocks ? 100 : 70, .65,
                _blocks ? CabinetMesh.Plastic() : CabinetMesh.BrushedSteel(), tint: _blocks);
            moving.Children.Add(CabinetMesh.Box(-143, top - 121, -3, FrontWidth, 121, 17, finish, _blocks ? 7 : 3));
            moving.Children.Add(CabinetMesh.Box(-138, top - 108, -168, 276, 7, 168, dark, 2));
            moving.Children.Add(CabinetMesh.Box(-140, top - 105, -168, 7, 89, 165, finish, 2));
            moving.Children.Add(CabinetMesh.Box(133, top - 105, -168, 7, 89, 165, finish, 2));
            moving.Children.Add(CabinetMesh.Box(-138, top - 105, -168, 276, 87, 5, edges, 2));
            // Telescoping runners are visible below the tray, along both sides.
            foreach (var x in new[] { -145d, 142 })
            {
                moving.Children.Add(CabinetMesh.Box(x, top - 82, -162, 3, 8, 155, chrome, 1));
                moving.Children.Add(CabinetMesh.Box(x, top - 67, -150, 3, 3, 140, chrome, 1));
            }
            if (!_blocks)
            {
                foreach (var x in new[] { -65d, 65 })
                {
                    moving.Children.Add(CabinetMesh.Box(x - 8, top - 86, 14, 16, 21, 5, chrome, 2));
                    moving.Children.Add(CabinetMesh.Cylinder(new(x, top - 76, 18), new(x, top - 76, 31), 4.5, chrome));
                }
                moving.Children.Add(CabinetMesh.Cylinder(new(-65, top - 76, 31), new(65, top - 76, 31), 5, chrome));
            }
            else
            {
                moving.Children.Add(CabinetMesh.Box(-40, top - 21, 14, 80, 16, 1, edges, 5));
                moving.Children.Add(CabinetMesh.Box(-34, top - 23, 15, 68, 3, 4, finish, 1));
            }
            Add(moving, part.Transform);
            var front = BuildFront(c);
            Host(front, CabinetMesh.Quad(new(-142, top, 14.4), new(-142, top - 121, 14.4), new(142, top - 121, 14.4), new(142, top, 14.4)), part.Transform);
            // On dudakta ince serit: ekle dugmesi ve bos durum metni.
            Host(part.Tray, CabinetMesh.Quad(new(-129, top + 3, -30), new(-129, top + 3, -6), new(129, top + 3, -6), new(129, top + 3, -30)), part.Transform);
            BuildCards(c, part, top);
            SetOpen(part, c.IsOpen);
        }
        UpdateLayout();
    }

    // Dondurme modeli "turntable": dikey eksen sabit (mobilya devrilmez), pivot dolabin merkezi.
    // Fare hedef aciyi degistirir, gorunum her karede hedefe yumusakca yaklasir; birakinca momentum azalarak surer.
    const double YawLimit = 55, PitchLimit = 25;
    double _targetYaw, _targetPitch, _velYaw, _velPitch, _fitWidth = 400;
    Point3D _pivot;
    // Zemin golgesi 2B katmanda: yere yatik 3B yuzey goz hizasindan bakinca cizgiye donusup kayboluyordu.
    // Ayak izinin ekrandaki izdusumu her kamera guncellemesinde hesaplanir; golge bakis acisindan bagimsiz gorunur.
    readonly Canvas _shadowLayer = new() { IsHitTestVisible = false };
    readonly System.Windows.Shapes.Ellipse _softShadow = new(), _contactShadow = new();
    (double X, double Y) _camOffset;
    /// <summary>DIP: dolabin altindaki seffaf bos alan (golge payi haric).</summary>
    public double BottomSlack { get; private set; }
    /// <summary>DIP: govdenin ekrandaki yatay siniri (oda duvarlarina carpismayi onlemek icin).</summary>
    public double BodyLeft { get; private set; }
    public double BodyRight { get; private set; } = 500;
    bool _animating, _dragRotating, _rightRotated, _built;
    Point _lastPos; DateTime _lastMove;
    Point? _rightDown;

    /// <summary>Varsayilan bakis (goz hizasina yakin, 6 derece: ust yuzey ince bir serit gorunur) uzerine kullanicinin yaw/pitch farki eklenir.</summary>
    Vector3D ViewDirection(double yawDeg, double pitchDeg)
    {
        // Varsayilan: dolap karsi duvarin dibinde, tam karsidan ve hafif yukaridan gorulur (yan aci yok).
        var yaw = yawDeg * Math.PI / 180;
        var elev = Math.Clamp((6 + pitchDeg) * Math.PI / 180, 4 * Math.PI / 180, 62 * Math.PI / 180);
        return new(Math.Sin(yaw) * Math.Cos(elev), Math.Sin(elev), Math.Cos(yaw) * Math.Cos(elev));
    }

    static (Vector3D Right, Vector3D Up) Basis(Vector3D view)
    {
        var forward = -view; forward.Normalize();
        var right = Vector3D.CrossProduct(forward, new(0, 1, 0)); right.Normalize();
        var up = Vector3D.CrossProduct(right, forward); up.Normalize();
        return (right, up);
    }

    /// <summary>Tum izinli acilarda dolabin (cekilmis cekmeceler dahil) en genis izdusumu: donerken olcek ve boyut sabit kalir.</summary>
    void ComputeFit()
    {
        var pts = new List<Point3D>();
        foreach (var x in new[] { -162d, 162 }) foreach (var y in new[] { 0d, _cabinetHeight + 27 }) foreach (var z in new[] { -182d, 22 }) pts.Add(new(x, y, z));
        // Cekilmis cekmece onu yalniz +-20 derece icinde kadraja alinir; asiri acilarda kenardan tasabilir (kadraj gereksiz genislemesin).
        var extracted = new List<Point3D>();
        foreach (var x in new[] { -145d, 145d }) foreach (var y in new[] { 10d, _cabinetHeight - 50 }) extracted.Add(new(x, y, 14 + Travel + 6));
        foreach (var x in new[] { -205d, 205 }) foreach (var y in new[] { -5d, _cabinetHeight + 45 }) pts.Add(new(x, y, -188));
        _pivot = new(-_side * 8, _cabinetHeight / 2, 0);
        // Ekran uzayinda tum acilarin birlesimi (asimetrik): kadraj sabit ama gereksiz bos alan yok.
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (var yaw = -YawLimit; yaw <= YawLimit; yaw += 5)
            foreach (var pitch in new[] { -PitchLimit, 0, PitchLimit })
            {
                var (r, u) = Basis(ViewDirection(yaw, pitch));
                foreach (var q in Math.Abs(yaw) <= 20 ? pts.Concat(extracted) : pts)
                {
                    var d = q - _pivot;
                    var px = Vector3D.DotProduct(d, r); var py = Vector3D.DotProduct(d, u);
                    minX = Math.Min(minX, px); maxX = Math.Max(maxX, px); minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                }
            }
        _fitWidth = maxX - minX + 20;
        _camOffset = ((minX + maxX) / 2, (minY + maxY) / 2);
        Width = _fitWidth * PixelsPerUnit;
        Height = (maxY - minY + 20) * PixelsPerUnit;
        // Varsayilan bakista dolabin altinda kalan bos (seffaf) pay: pencere bu kadar ekran altina tasabilir.
        var (_, u0) = Basis(ViewDirection(0, 0));
        BottomSlack = Math.Max(0, (pts.Min(q => Vector3D.DotProduct(q - _pivot, u0)) - minY) * PixelsPerUnit - 30);
    }

    void UpdateCamera()
    {
        var viewDirection = ViewDirection(_yaw, _pitch);
        var (r, u) = Basis(viewDirection);
        var center = _pivot + r * _camOffset.X + u * _camOffset.Y;
        _view.Camera = new OrthographicCamera(center + viewDirection * 1100, -viewDirection, new(0, 1, 0), _fitWidth);
        UpdateShadow(center, r, u);
    }

    static Brush Shadow(double inner, double mid)
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new(Color.FromArgb((byte)inner, 0, 0, 0), 0));
        b.GradientStops.Add(new(Color.FromArgb((byte)mid, 0, 0, 0), .55));
        b.GradientStops.Add(new(Color.FromArgb(0, 0, 0, 0), 1));
        b.Freeze(); return b;
    }

    /// <summary>Ayak izini (x +-175, z -200..50, y=0) ekrana izdusurur; genis yumusak golge + ayak altinda koyu temas golgesi.</summary>
    void UpdateShadow(Point3D center, Vector3D r, Vector3D u)
    {
        if (double.IsNaN(Width) || double.IsNaN(Height)) return;
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var x in new[] { -175d, 175 })
            foreach (var z in new[] { -200d, 50 })
            {
                var d = new Point3D(x, 0, z) - center;
                var sx = Width / 2 + Vector3D.DotProduct(d, r) * PixelsPerUnit;
                var sy = Height / 2 - Vector3D.DotProduct(d, u) * PixelsPerUnit;
                minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx); minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
            }
        double bl = double.MaxValue, br = double.MinValue;
        foreach (var x in new[] { -158d, 158 }) foreach (var y in new[] { 0d, _cabinetHeight + 27 }) foreach (var z in new[] { -180d, 22 })
            {
                var bx = Width / 2 + Vector3D.DotProduct(new Point3D(x, y, z) - center, r) * PixelsPerUnit;
                bl = Math.Min(bl, bx); br = Math.Max(br, bx);
            }
        BodyLeft = bl; BodyRight = br;
        double cx = (minX + maxX) / 2, w = maxX - minX, h = Math.Max(maxY - minY, 14);
        Place(_softShadow, cx, (minY + maxY) / 2 + 4, w * 1.25, h * 1.5 + 26);
        Place(_contactShadow, cx, maxY - h * 0.3, w * 1.02, Math.Max(16, h * 0.7));
    }

    static void Place(FrameworkElement e, double cx, double cy, double w, double h)
    {
        e.Width = w; e.Height = h;
        Canvas.SetLeft(e, cx - w / 2); Canvas.SetTop(e, cy - h / 2);
    }

    /// <summary>Kaydedilmis donusu uygular (yeniden insa etmeden).</summary>
    public void SetView(double yaw, double pitch)
    {
        if (_dragRotating || _animating) return; // kullanici donduruyorsa ayar yeniden uygulamasi araya girmesin
        _yaw = _targetYaw = Math.Clamp(yaw, -YawLimit, YawLimit);
        _pitch = _targetPitch = Math.Clamp(pitch, -PitchLimit, PitchLimit);
        if (_cabinetHeight > 0) UpdateCamera();
    }

    /// <summary>Animasyonla hedef aciya doner (sifirlama, klavye).</summary>
    public void RotateTo(double yaw, double pitch)
    {
        _targetYaw = Math.Clamp(yaw, -YawLimit, YawLimit); _targetPitch = Math.Clamp(pitch, -PitchLimit, PitchLimit);
        _velYaw = _velPitch = 0;
        if (_reduceMotion) { _yaw = _targetYaw; _pitch = _targetPitch; UpdateCamera(); ViewChanged?.Invoke(_yaw, _pitch); }
        else StartAnimating();
    }

    void BeginRotate(Point p)
    {
        _dragRotating = true; _lastPos = p; _lastMove = DateTime.UtcNow;
        _targetYaw = _yaw; _targetPitch = _pitch; _velYaw = _velPitch = 0;
        CaptureMouse();
        if (!_reduceMotion) StartAnimating();
    }

    void RotateMove(Point p)
    {
        var d = p - _lastPos; _lastPos = p; _lastMove = DateTime.UtcNow;
        var dy = -d.X * .35; var dp = d.Y * .25;
        _targetYaw = Math.Clamp(_targetYaw + dy, -YawLimit, YawLimit);
        _targetPitch = Math.Clamp(_targetPitch + dp, -PitchLimit, PitchLimit);
        _velYaw = _velYaw * .5 + dy * .5; _velPitch = _velPitch * .5 + dp * .5;
        if (_reduceMotion) { _yaw = _targetYaw; _pitch = _targetPitch; UpdateCamera(); }
    }

    void EndRotate()
    {
        if (!_dragRotating) return;
        _dragRotating = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        // Birakmadan once fare durduysa momentum yok.
        if (_reduceMotion || DateTime.UtcNow - _lastMove > TimeSpan.FromMilliseconds(80)) _velYaw = _velPitch = 0;
        if (_reduceMotion) ViewChanged?.Invoke(_yaw, _pitch);
    }

    void StartAnimating()
    {
        if (_animating) return;
        _animating = true;
        CompositionTarget.Rendering += Tick; // yalniz hareket varken; durunca cikarilir (bosta cizim yok)
    }

    void Tick(object? sender, EventArgs e)
    {
        if (!_dragRotating)
        {
            _targetYaw += _velYaw; _targetPitch += _velPitch;
            if (Math.Abs(_targetYaw) >= YawLimit) { _targetYaw = Math.Clamp(_targetYaw, -YawLimit, YawLimit); _velYaw = 0; }
            if (Math.Abs(_targetPitch) >= PitchLimit) { _targetPitch = Math.Clamp(_targetPitch, -PitchLimit, PitchLimit); _velPitch = 0; }
            _velYaw *= .9; _velPitch *= .9;
        }
        _yaw += (_targetYaw - _yaw) * .3; _pitch += (_targetPitch - _pitch) * .3;
        UpdateCamera();
        if (_dragRotating || Math.Abs(_velYaw) > .02 || Math.Abs(_velPitch) > .02
            || Math.Abs(_targetYaw - _yaw) > .05 || Math.Abs(_targetPitch - _pitch) > .05) return;
        _yaw = _targetYaw; _pitch = _targetPitch; UpdateCamera();
        CompositionTarget.Rendering -= Tick; _animating = false;
        ViewChanged?.Invoke(_yaw, _pitch);
    }

    /// <summary>Zemin golgesi: genis yumusak yayilma + ayaklarin altinda koyu temas cekirdegi.</summary>
    void AddFloorShadow()
    {
        var k = _lowQuality ? .6 : 1.0;
        _softShadow.Fill = Shadow(190 * k, 110 * k); _contactShadow.Fill = Shadow(245 * k, 185 * k);
        if (_shadowLayer.Children.Count == 0) { _shadowLayer.Children.Add(_softShadow); _shadowLayer.Children.Add(_contactShadow); }
        // Duvar golgesi: dolap arka duvara yasli; govdenin arkasinda yumusak koyuluk (karsidan bakista kenarlarda halka).
        var wall = new RadialGradientBrush();
        wall.GradientStops.Add(new(Color.FromArgb((byte)(150 * k), 0, 0, 0), 0));
        wall.GradientStops.Add(new(Color.FromArgb((byte)(95 * k), 0, 0, 0), .62));
        wall.GradientStops.Add(new(Color.FromArgb(0, 0, 0, 0), 1));
        wall.Freeze();
        var wallMaterial = new DiffuseMaterial(wall) { AmbientColor = Colors.Black };
        var top = _cabinetHeight + 60;
        var model = new Model3DGroup();
        model.Children.Add(new GeometryModel3D(CabinetMesh.Quad(new(-225, top, -188), new(-225, -15, -188), new(225, -15, -188), new(225, top, -188)), wallMaterial));
        Add(model);
    }

    void Header(double height, Material body)
    {
        var panel = new Grid { Width = 286, Height = 38, Background = Brushes.Transparent };
        panel.MouseLeftButtonDown += (s, e) => HeaderDown?.Invoke(s, e);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (glyph, name, action) in new (string, string, RoutedEventHandler)[]
        { ("\uE721", "Ara", (s,e) => SearchClick?.Invoke(s,e)), ("\uE710", "Kategori ekle", (s,e) => AddCategoryClick?.Invoke(s,e)), ("\uE713", "Ayarlar", (s,e) => SettingsClick?.Invoke(s,e)) })
        {
            var b = new Button { Content = glyph, ToolTip = name, Style = (Style)FindResource("HeaderButtonStyle") };
            AutomationProperties.SetName(b, name); b.Click += action; buttons.Children.Add(b);
        }
        // Dort yon oku: basili tutunca donmeye devam eder (sol/sag yaw, yukari/asagi bakis acisi).
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 6, 0) };
        foreach (var (glyph, name, dy, dp) in new (string, string, double, double)[]
        {
            ("\uE76B", "Sola çevir", 6, 0), ("\uE70E", "Yukarı eğ", 0, -4),
            ("\uE70D", "Aşağı eğ", 0, 4), ("\uE76C", "Sağa çevir", -6, 0),
        })
        {
            var b = new RepeatButton { Content = glyph, Style = (Style)FindResource("HeaderRepeatStyle"), Delay = 250, Interval = 50,
                ToolTip = name + " (basılı tut). Gövdeyi sürükleyerek de çevirebilirsin. Açıyı sıfırlamak: Ayarlar > Yerleşim." };
            AutomationProperties.SetName(b, name);
            b.Click += (_, _) => RotateTo(_targetYaw + dy, _targetPitch + dp);
            arrows.Children.Add(b); ArrowButtons.Add(b);
        }
        // Yon oklari solda ayri grup, ara/ekle/ayarlar sagda: iki islev birbirine karismaz.
        arrows.HorizontalAlignment = HorizontalAlignment.Left; arrows.VerticalAlignment = VerticalAlignment.Center; arrows.Margin = new(8, 0, 0, 0);
        panel.Children.Add(arrows);
        panel.Children.Add(buttons);
        var headerBody = new Model3DGroup(); headerBody.Children.Add(CabinetMesh.Box(-143, height - 51, -4, 286, 38, 17, body, 2)); Add(headerBody);
        Host(panel, CabinetMesh.Quad(new(-143, height - 13, 13.5), new(-143, height - 51, 13.5), new(143, height - 51, 13.5), new(143, height - 13, 13.5)));
    }

    Button BuildFront(CategoryVm category)
    {
        var button = new Button { DataContext = category, Width = 284, Height = 121, Style = (Style)FindResource(_blocks ? "PhysicalBlockFront" : "PhysicalMetalFront"), ContextMenu = new ContextMenu(), AllowDrop = true };
        AutomationProperties.SetName(button, category.Automation);
        button.Click += (s, e) => FrontClick?.Invoke(s, e);
        button.ContextMenuOpening += (s, e) => FrontContext?.Invoke(s, e);
        _frontButtons.Add(button);
        WireDrop(button); return button;
    }

    FrameworkElement BuildTray(CategoryVm category)
    {
        var layout = new DockPanel { LastChildFill = true };
        var add = new Button { Content = "+ Kısayol ekle", DataContext = category, Style = (Style)FindResource("ModalButtonStyle"), FontSize = 11, Padding = new(8, 1, 8, 1) };
        AutomationProperties.SetName(add, category.Name + " çekmecesine kısayol ekle"); add.Click += (s, e) => AddClick?.Invoke(s, e);
        DockPanel.SetDock(add, Dock.Right); layout.Children.Add(add);
        layout.Children.Add(new TextBlock
        {
            Text = category.Items.Count == 0 ? "Dosya, klasör veya uygulama sürükleyip bırak" : "",
            FontSize = 10.5, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(4, 0, 6, 0),
        });
        var border = new Border { DataContext = category, Width = 280, Height = 26, Padding = new(4, 2, 4, 2), CornerRadius = new(4), Background = new SolidColorBrush(Color.FromArgb(220, 16, 19, 25)), Child = layout, AllowDrop = true };
        WireDrop(border); return border;
    }

    /// <summary>Kisayollar dosya dolabindaki evrak gibi dik durur: 4 sutun, arka siralar biraz daha yuksekte.
    /// shortcut: 3 siradan fazlasi (12+ oge) yukari dogru uzar, kaydirma yok; cok ogeli cekmecede sayfalama eklenmeli.</summary>
    void BuildCards(CategoryVm category, DrawerPart part, double top)
    {
        const double W = 62, H = 74;
        for (var i = 0; i < category.Items.Count; i++)
        {
            var item = category.Items[i];
            int col = i % 4, row = i / 4;
            var xc = -99 + col * 66; var z = -46 - row * 46; var y0 = top - 10 + row * 30;
            var button = new Button { DataContext = item, Style = (Style)FindResource("ItemButtonStyle"), ContextMenu = new ContextMenu(), ToolTip = item.Subtitle };
            AutomationProperties.SetName(button, item.Automation + ". Bir kez seç, tekrar açmak için Enter");
            var lift = new TranslateTransform3D();
            _lifts[button] = lift;
            // Ilk tik evragi yukari kaldirir, kalkmis evraga ikinci tik hedefi acar.
            button.Click += (s, e) => { if (_lifted == button) ItemClick?.Invoke(s, e); else Lift(button); };
            button.GotKeyboardFocus += (_, _) => Lift(button);
            button.PreviewMouseLeftButtonDown += (_, e) => { _pressCard = button; _pressPos = e.GetPosition(this); };
            button.ContextMenuOpening += (s, e) => ItemContext?.Invoke(s, e);
            var card = new Border
            {
                Child = button, CornerRadius = new(9), Padding = new(0), Width = 86, Height = 102,
                BorderThickness = new(1, 1.5, 1, 0.5),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(170, 230, 238, 250), Color.FromArgb(40, 230, 238, 250), 90),
                Background = new LinearGradientBrush(Color.FromArgb(150, 58, 66, 80), Color.FromArgb(200, 14, 17, 23), 90),
            };
            WireDrop(card);
            part.Cards.Add((button, lift));
            var group = new Transform3DGroup(); group.Children.Add(lift); group.Children.Add(part.Transform);
            Host(card, CabinetMesh.Quad(new(xc - W / 2, y0 + H, z), new(xc - W / 2, y0, z), new(xc + W / 2, y0, z), new(xc + W / 2, y0 + H, z)), group);
        }
    }

    // Surukle-sirala: basili kart 8 px'ten fazla tasininca baslar. Yakalama birakilir ki imlecin altindaki
    // 3B yuzeydeki denetim (Mouse.DirectlyOver) hedef olarak okunabilsin.
    void StartReorder()
    {
        _reordering = true;
        _pressCard!.ReleaseMouseCapture();
        Lift(_pressCard);
        Mouse.OverrideCursor = Cursors.Hand;
    }

    void UpdateDropTarget()
    {
        Button? target = null;
        for (var d = Mouse.DirectlyOver as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is Button b && b != _pressCard && b.DataContext is ShortcutVm or CategoryVm) { target = b; break; }
        if (target == _dropTarget) return;
        if (_dropTarget != null) _dropTarget.Opacity = 1;
        _dropTarget = target;
        if (_dropTarget != null) _dropTarget.Opacity = .55; // birakilacak yer belli olsun
    }

    void EndReorder()
    {
        var src = (ShortcutVm)_pressCard!.DataContext;
        var target = _dropTarget;
        if (target != null) target.Opacity = 1;
        _reordering = false; _pressCard = null; _dropTarget = null;
        Mouse.OverrideCursor = null;
        if (target?.DataContext is ShortcutVm t) ShortcutReordered?.Invoke(src, t);
        else if (target?.DataContext is CategoryVm c) ShortcutMoved?.Invoke(src, c);
    }

    void Lift(Button button)
    {
        if (_lifted == button) return;
        if (_lifted != null && _lifts.TryGetValue(_lifted, out var old)) Animate(old, TranslateTransform3D.OffsetYProperty, 0);
        _lifted = button;
        Animate(_lifts[button], TranslateTransform3D.OffsetYProperty, LiftHeight);
    }

    /// <summary>Bir cekmece kapanip digeri acilinca ayni turda tek ses: acilma kazanir.</summary>
    void QueueSound(bool open)
    {
        if (!Sounds) return;
        _soundOpen |= open;
        if (_soundQueued) return;
        _soundQueued = true;
        Dispatcher.BeginInvoke(() => { DrawerSounds.Play(_blocks, _soundOpen); _soundQueued = _soundOpen = false; });
    }

    void SetOpen(DrawerPart part, bool open)
    {
        foreach (var e in part.Cards.Select(c => (FrameworkElement)c.Button.Parent).Prepend(part.Tray))
        {
            e.IsHitTestVisible = open; e.IsEnabled = open;
            e.Visibility = open ? Visibility.Visible : Visibility.Hidden;
        }
        if (open) return;
        foreach (var (button, lift) in part.Cards)
        {
            if (_lifted == button) _lifted = null;
            Animate(lift, TranslateTransform3D.OffsetYProperty, 0);
        }
    }

    void WireDrop(UIElement e)
    {
        e.DragEnter += (s, a) => DrawerEnter?.Invoke(s, a);
        e.DragLeave += (s, a) => DrawerLeave?.Invoke(s, a);
        e.Drop += (s, a) => DrawerDrop?.Invoke(s, a);
    }

    void Add(Model3DGroup model, Transform3D? transform = null)
    {
        model.Freeze(); _view.Children.Add(new ModelVisual3D { Content = model, Transform = transform ?? Transform3D.Identity });
    }

    double TextureScale => _lowQuality ? 1.5 : 3;

    void Host(FrameworkElement visual, MeshGeometry3D mesh, Transform3D? transform = null)
    {
        // Diffuse host preserves alpha: an emissive host would add light to faces behind labels.
        var material = new DiffuseMaterial(Brushes.White);
        Viewport2DVisual3D.SetIsVisualHostMaterial(material, true);
        // Controls on surfaces participate in focus, input and accessibility normally.
        RenderOptions.SetCachingHint(visual, CachingHint.Cache);
        RenderOptions.SetBitmapScalingMode(visual, _lowQuality ? BitmapScalingMode.LowQuality : BitmapScalingMode.HighQuality);
        // 2D yuzeyler 3D'ye doku olarak basilir; LayoutTransform ile 2x buyuk cizilir ki yazi ve ikonlar net kalsin.
        var host = new Grid { LayoutTransform = new ScaleTransform(TextureScale, TextureScale), Cursor = Cursors.Arrow };
        host.Children.Add(visual);
        _view.Children.Add(new Viewport2DVisual3D { Geometry = mesh, Material = material, Visual = host, Transform = transform ?? Transform3D.Identity });
    }

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
