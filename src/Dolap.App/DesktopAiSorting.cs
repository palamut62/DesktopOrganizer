using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Dolap.Core;

namespace Dolap.App;

public partial class MainWindow
{
    readonly HashSet<Guid> _sortPending = [];
    readonly CancellationTokenSource _sortCancel = new();
    DispatcherTimer? _sortTimer;
    bool _sorting;

    void QueueDesktopSort(IEnumerable<Guid> ids)
    {
        if (_isolated || !_store.LoadUsability().AutoClassifyDesktop) return;
        foreach (var id in ids) _sortPending.Add(id);
        if (_sortTimer == null)
        {
            _sortTimer = new() { Interval = TimeSpan.FromSeconds(5) };
            _sortTimer.Tick += async (_, _) => { _sortTimer.Stop(); await SortDesktop(); };
        }
        _sortTimer.Stop(); _sortTimer.Start();
    }

    static string DrawerRoot => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Masaüstü Dolabı");

    /// <summary>Yalniz dogrudan masaustundeki ogeyi cekmecenin klasorune tasir ve kisayolu yeni yola baglar.</summary>
    internal FileMove? Relocate(Shortcut s, Category target)
    {
        if (s.Kind == ShortcutKind.Url || target.LinkedFolder != null) return null;
        var src = s.SourceLinkPath ?? s.Target;
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(src)), desktop, StringComparison.OrdinalIgnoreCase)) return null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or System.IO.PathTooLongException) { return null; }
        if (DesktopMover.Move(src, DesktopMover.DrawerFolder(DrawerRoot, target.Name)) is not { } m) return null;
        try { _store.UpdateShortcut(s.Id, ShortcutFactory.FromPath(m.To) with { DisplayName = s.DisplayName }); }
        catch { DesktopMover.Undo(m); throw; }
        return m;
    }

    async Task SortDesktop()
    {
        if (_sorting || _closing) return;
        var prefs = _store.LoadUsability();
        if (!prefs.AutoClassifyDesktop) { _sortPending.Clear(); return; }
        if (string.IsNullOrWhiteSpace(prefs.AssistantModel) || string.IsNullOrWhiteSpace(AssistantCredentials.Read()))
        { ShowToast("Dosyalar Gelenler çekmecesinde. Akıllı sınıflandırma için OpenRouter anahtarı ve model seçin."); _sortPending.Clear(); return; }
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        var used = prefs.AutoClassifyDay == day ? prefs.AutoClassifyRequests : 0;
        if (used >= 20) { ShowToast("Günlük 20 otomatik sınıflandırma isteği sınırına ulaşıldı. Dosyalar Gelenler'de kalır."); _sortPending.Clear(); return; }
        var items = _store.GetShortcuts().Where(s => _sortPending.Contains(s.Id)).Take(30).ToList();
        foreach (var item in items) _sortPending.Remove(item.Id);
        if (items.Count == 0) { _sortPending.Clear(); return; }
        var destinations = _store.GetCategories().Where(c => c.LinkedFolder == null).ToList();
        if (destinations.Count == 0) { ShowToast("Sınıflandırma için en az bir normal çekmece oluşturun."); return; }
        _sorting = true;
        try
        {
            _store.SaveUsability(prefs with { AutoClassifyDay = day, AutoClassifyRequests = used + 1 });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var client = new OpenRouterClient(http, AssistantCredentials.Read);
            var state = JsonSerializer.Serialize(new
            {
                Categories = destinations.Select(c => new { c.Id, c.Name }),
                Shortcuts = items.Select(s => new { s.Id, s.DisplayName, s.Kind, Extension = System.IO.Path.GetExtension(s.Target) })
            });
            var result = await client.Plan(prefs.AssistantModel,
                "Yeni masaüstü öğelerini mevcut normal çekmecelere sınıflandır. Yalnızca move_shortcuts işlemleri öner; uygun hedef yoksa o öğe için işlem önerme. Uygulama taşınan öğeyi çekmecenin klasörüne koyar.", state, _sortCancel.Token);
            if (_closing || !_store.LoadUsability().AutoClassifyDesktop) return;
            var assignments = DesktopClassification.Validate(result.Plan, items, _store.GetCategories());
            var current = _store.GetShortcuts().ToDictionary(s => s.Id);
            var cats = _store.GetCategories().ToDictionary(c => c.Id);
            var moves = new List<FileMove>(); var kept = 0;
            try
            {
                _store.EditShortcuts("Akıllı masaüstü yerleştirme", () =>
                {
                    foreach (var assignment in assignments)
                    {
                        var source = items.Single(s => s.Id == assignment.ShortcutId);
                        if (!current.TryGetValue(source.Id, out var now) || source != now) continue;
                        // Tasinamayan (acik/kullanimda) oge Gelenler'de kalir; yarim tasima olmaz.
                        if (Relocate(now, cats[assignment.CategoryId]) is not { } m) { kept++; continue; }
                        moves.Add(m); _store.MoveShortcut(source.Id, assignment.CategoryId);
                    }
                    return moves.Count == 0 ? null : () => { foreach (var m in Enumerable.Reverse(moves)) DesktopMover.Undo(m); };
                });
            }
            catch { foreach (var m in Enumerable.Reverse(moves)) DesktopMover.Undo(m); throw; }
            RefreshCollection();
            ShowToast($"{moves.Count} öğe çekmece klasörüne taşındı (Belgeler\\Masaüstü Dolabı)." + (kept > 0 ? $" {kept} öğe kullanımda, Gelenler'de kaldı." : ""), undo: moves.Count > 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DolapException or JsonException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        { if (!_closing) ShowToast("Akıllı yerleştirme yapılamadı; dosyalar Gelenler'de duruyor. " + ex.Message); }
        finally
        {
            _sorting = false;
            if (!_closing && _sortPending.Count > 0) _sortTimer?.Start();
        }
    }
}
