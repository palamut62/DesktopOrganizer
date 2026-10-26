using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dolap.Core;

namespace Dolap.App;

public sealed class AssistantWindow : Window
{
    readonly MainWindow _host;
    readonly CollectionStore _store;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    readonly CancellationTokenSource _cancel = new();
    readonly OpenRouterClient _client;
    readonly PasswordBox _key = new() { Width = 230, Margin = new(4) };
    readonly ComboBox _models = new() { DisplayMemberPath = "Label", IsEditable = true, IsTextSearchEnabled = true, Margin = new(0, 5, 0, 5) };
    readonly TextBlock _modelInfo = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSteelBlue };
    readonly TextBox _prompt = new() { AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBox _result = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSteelBlue, Margin = new(0, 6, 0, 0) };
    readonly Button _apply, _generate, _test, _refresh, _undo;
    AssistantPlan? _plan;
    IDisposable? _checkpoint;
    List<FileMove> _moves = [];
    string _afterState = "";
    string _state = "";
    bool _busy;
    readonly string _cache = Path.Combine(Paths.Data, "openrouter-models.json");

    public AssistantWindow(MainWindow host, bool loadModels = true)
    {
        _host = host; _store = host.Store; _client = new(_http, AssistantCredentials.Read);
        Title = "Dolap Asistanı · OpenRouter"; Width = 800; Height = 760; MinWidth = 640; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = (Brush)Application.Current.FindResource("CardBrush"); Foreground = Brushes.White;
        var root = new DockPanel { Margin = new(18) }; var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "Dolap Asistanı", FontSize = 22, FontWeight = FontWeights.SemiBold });
        top.Children.Add(new TextBlock { Text = "İstek gönderildiğinde öğe adları ve dolap ayarları OpenRouter'a iletilir. Dosya içeriği ve tam dosya yolları gönderilmez. Güvenli işlemler hemen uygulanır ve geri alınabilir. Silme, program açma, sistem ayarları ve toplu değişiklikler önce onay ister.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
        var keys = new WrapPanel(); keys.Children.Add(new TextBlock { Text = "API anahtarı", VerticalAlignment = VerticalAlignment.Center }); keys.Children.Add(_key);
        keys.Children.Add(Button("Anahtarı kaydet", () => Safe(() => { AssistantCredentials.Save(_key.Password); _key.Clear(); _status.Text = "Anahtar Windows Kimlik Bilgileri Yöneticisi'ne kaydedildi."; })));
        keys.Children.Add(Button("Anahtarı kaldır", () => { AssistantCredentials.Delete(); _key.Clear(); _status.Text = "Kayıtlı anahtar kaldırıldı."; })); top.Children.Add(keys);
        top.Children.Add(new TextBlock { Text = "Model (arama için yazabilirsiniz)", Margin = new(0, 6, 0, 0) }); top.Children.Add(_models); top.Children.Add(_modelInfo);
        var controls = new WrapPanel();
        _refresh = Button("Modelleri yenile", async () => await LoadModels(true)); controls.Children.Add(_refresh);
        _test = Button("Modeli test et", async () => await Generate(true)); controls.Children.Add(_test); top.Children.Add(controls);
        top.Children.Add(new TextBlock { Text = "İsteğin (ör. Masaüstüne Gelenler çekmecesindeki ZIP dosyalarını Arşiv'e yerleştir)", Margin = new(0, 10, 0, 5), TextWrapping = TextWrapping.Wrap });
        top.Children.Add(_prompt); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var bottom = new StackPanel(); var actions = new WrapPanel();
        _generate = Button("Plan oluştur", async () => await Generate(false)); actions.Children.Add(_generate);
        _apply = Button("Gösterilen planı uygula", Apply); _apply.IsEnabled = false; actions.Children.Add(_apply);
        actions.Children.Add(Button("Planı iptal et", () => { _plan = null; _apply.IsEnabled = false; _result.Clear(); }));
        _undo = Button("Son işlemi geri al", Undo); _undo.IsEnabled = false; actions.Children.Add(_undo);
        bottom.Children.Add(actions); bottom.Children.Add(_status); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var planTitle = new TextBlock { Text = "Plan ve sonuç", Margin = new(0, 10, 0, 0) };
        DockPanel.SetDock(planTitle, Dock.Top); root.Children.Add(planTitle);
        _result.Margin = new(0, 5, 0, 8); root.Children.Add(_result); Content = root;
        _models.SelectionChanged += (_, _) =>
        {
            if (_models.SelectedItem is RouterModel model)
            { _modelInfo.Text = model.Details; _store.SaveUsability(_store.LoadUsability() with { AssistantModel = model.Id }); }
            _plan = null; _apply.IsEnabled = false;
        };
        if (loadModels) Loaded += async (_, _) => await LoadModels(false);
        Closed += (_, _) => { _cancel.Cancel(); _http.Dispose(); _checkpoint?.Dispose(); };
    }
    static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.FindResource("ModalButtonStyle"), Margin = new(2), Padding = new(9, 5, 9, 5) };
        button.Click += (_, _) => action(); return button;
    }
    void Safe(Action action) { try { action(); } catch (Exception ex) { _status.Text = ex.Message; } }
    void Busy(bool busy)
    {
        _busy = busy; _generate.IsEnabled = _test.IsEnabled = _refresh.IsEnabled = _models.IsEnabled = !busy;
        _apply.IsEnabled = !busy && _plan?.Actions.Count > 0;
    }
    async Task LoadModels(bool force)
    {
        if (_busy) return; Busy(true);
        try
        {
            List<RouterModel>? models = null; bool cached = false;
            if (!force && File.Exists(_cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(_cache) < TimeSpan.FromHours(24))
                try { models = JsonSerializer.Deserialize<List<RouterModel>>(await File.ReadAllTextAsync(_cache, _cancel.Token)); cached = true; } catch (JsonException) { }
            if (models == null)
            {
                try
                {
                    models = await _client.Models(_cancel.Token);
                    Directory.CreateDirectory(Paths.Data);
                    await File.WriteAllTextAsync(_cache + ".tmp", JsonSerializer.Serialize(models), _cancel.Token); File.Move(_cache + ".tmp", _cache, true);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DolapException)
                {
                    if (_cancel.IsCancellationRequested) return;
                    if (!File.Exists(_cache)) throw;
                    models = JsonSerializer.Deserialize<List<RouterModel>>(await File.ReadAllTextAsync(_cache, _cancel.Token)); cached = true;
                }
            }
            _models.ItemsSource = models;
            _models.SelectedItem = models?.FirstOrDefault(m => m.Id == _store.LoadUsability().AssistantModel);
            _status.Text = cached ? "Önbellekteki model listesi kullanılıyor. Güncel liste için Yenile." : "Model listesi OpenRouter'dan yenilendi. Bir model seçin.";
        }
        catch (Exception ex) { if (!_cancel.IsCancellationRequested) _status.Text = "Model listesi yüklenemedi: " + ex.Message; }
        finally { if (!_cancel.IsCancellationRequested) Busy(false); }
    }
    async Task Generate(bool test)
    {
        if (_busy) return;
        if (_models.SelectedItem is not RouterModel { Structured: true } model)
        { _status.Text = "JSON şeması (structured_outputs) destekleyen bir model seçin."; return; }
        if (!test && string.IsNullOrWhiteSpace(_prompt.Text)) { _status.Text = "Önce isteğinizi yazın."; return; }
        _plan = null; Busy(true); _status.Text = "Yanıt bekleniyor…";
        try
        {
            _state = test ? "{}" : _store.AssistantState();
            var result = await _client.Plan(model.Id, test ? "Bu bağlantı testidir. Message: Bağlantı başarılı. Actions boş olsun." : _prompt.Text, _state, _cancel.Token);
            if (_cancel.IsCancellationRequested) return;
            if (test)
            {
                if (result.Plan.Actions.Count != 0) throw new DolapException("Model testte beklenmeyen işlem önerdi; uygulanmadı.");
                _result.Text = "Model, uygulamanın gerçek JSON şemasıyla yanıt verdi. Hiçbir işlem uygulanmadı.";
            }
            else
            {
                var steps = _store.PreviewAssistant(result.Plan);
                _plan = result.Plan;
                _result.Text = result.Plan.Message + "\n\n" + string.Join("\n", steps.Select((s, i) => $"{i + 1}. {s}"));
            }
            _status.Text = $"Süre: {result.Elapsed.TotalSeconds:0.0} sn · Maliyet: {(result.Cost is { } cost ? "$" + cost.ToString("0.######") : "sağlayıcı bildirmedi")}";
            if (_plan is { Actions.Count: > 0 } plan)
            {
                if (AssistantProtocol.NeedsConfirmation(plan)) _status.Text += "\nSilme, açma, sistem ayarı veya toplu değişiklik içeriyor: kontrol edip 'Gösterilen planı uygula'ya basın.";
                else ApplyPlan();
            }
        }
        catch (Exception ex) { if (!_cancel.IsCancellationRequested) _status.Text = "İşlem uygulanmadı: " + ex.Message; }
        finally { if (!_cancel.IsCancellationRequested) Busy(false); }
    }
    void Apply() => Safe(() => { if (!_busy && _plan != null) ApplyPlan(); });

    void ApplyPlan()
    {
        var plan = _plan!;
        if (_store.AssistantState() != _state) { _plan = null; _apply.IsEnabled = false; throw new DolapException("Koleksiyon değişti; güncel durumla yeni bir plan oluşturun."); }
        var backup = Path.Combine(Paths.Backups, $"asistan-oncesi-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Paths.Backups); _store.ExportJson(backup);
        var inbox = _store.LoadUsability().DesktopInboxCategory;
        var before = _store.GetShortcuts().ToDictionary(s => s.Id);
        var checkpoint = _store.Checkpoint();
        try { _store.ApplyAssistant(plan); } catch { checkpoint.Dispose(); throw; }
        _checkpoint?.Dispose(); _checkpoint = checkpoint; _moves = [];
        _plan = null; _apply.IsEnabled = false;
        // Gelenler'den normal cekmeceye tasinan masaustu ogesi fiziksel olarak cekmecenin klasorune gider.
        var kept = 0;
        var cats = _store.GetCategories().ToDictionary(c => c.Id);
        foreach (var id in plan.Actions.Where(a => a.Kind == "move_shortcuts").SelectMany(a => a.Ids))
        {
            if (!Guid.TryParse(id, out var sid) || !before.TryGetValue(sid, out var old) || old.CategoryId != inbox) continue;
            var now = _store.GetShortcuts().FirstOrDefault(s => s.Id == sid);
            if (now == null || !cats.TryGetValue(now.CategoryId, out var cat) || cat.LinkedFolder != null) continue;
            if (_host.Relocate(now, cat) is { } m) _moves.Add(m); else kept++;
        }
        // launch yalniz onay isteyen planda bulunur (NeedsConfirmation); Uygula tiklamasi onaydir.
        var opened = new List<string>();
        foreach (var id in plan.Actions.Where(a => a.Kind == "launch").SelectMany(a => a.Ids).Distinct())
            if (Guid.TryParse(id, out var sid) && _store.GetShortcuts().FirstOrDefault(s => s.Id == sid) is { } sc)
                opened.Add(ShellLauncher.Launch(sc).Message);
        (Application.Current as App)?.RefreshAfterAssistant();
        _afterState = _store.AssistantState(); _undo.IsEnabled = true;
        _status.Text = "Uygulandı." + (_moves.Count > 0 ? $" {_moves.Count} dosya Belgeler\\Masaüstü Dolabı altına taşındı." : "")
            + (kept > 0 ? $" {kept} dosya kullanımda olduğu için masaüstünde kaldı." : "")
            + (opened.Count > 0 ? " " + string.Join(" ", opened) : "") + "\nGeri almak için 'Son işlemi geri al'. Yedek: " + backup;
    }

    void Undo() => Safe(() =>
    {
        if (_checkpoint == null) return;
        if (_store.AssistantState() != _afterState)
            throw new DolapException("Asistandan sonra başka değişiklik oldu; geri dönmek için Ayarlar > Veri > Yedekten geri yükle.");
        foreach (var m in Enumerable.Reverse(_moves)) DesktopMover.Undo(m);
        _store.Restore(_checkpoint); _checkpoint.Dispose(); _checkpoint = null; _moves = []; _undo.IsEnabled = false;
        (Application.Current as App)?.RefreshAfterAssistant();
        _status.Text = "Asistanın son işlemi geri alındı; taşınan dosyalar masaüstüne döndü.";
    });
}
