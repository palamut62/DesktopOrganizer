using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Dolap.Core;

public sealed record RouterModel(string Id, string Name, int Context, decimal InputPrice, decimal OutputPrice, bool Structured)
{
    public string Label => $"{Name} ({Id})" + (Structured ? "" : " · bu uygulamada kullanılamaz");
    static string Price(decimal p) => p < 0 ? "değişken" : p == 0 ? "ücretsiz" : "$" + (p * 1000000).ToString("0.######", CultureInfo.InvariantCulture);
    public string Details => $"Bağlam: {Context:N0} · 1M token giriş: {Price(InputPrice)} / çıkış: {Price(OutputPrice)} · JSON şeması: {(Structured ? "var" : "yok (plan için gerekli)")}";
}
public sealed record RouterResult(AssistantPlan Plan, TimeSpan Elapsed, decimal? Cost);

public sealed class OpenRouterClient(HttpClient http, Func<string> readKey)
{
    const string Endpoint = "https://openrouter.ai/api/v1/";
    public async Task<List<RouterModel>> Models(CancellationToken token)
    {
        using var response = await http.GetAsync(Endpoint + "models", token);
        if (!response.IsSuccessStatusCode) throw new DolapException($"Model listesi alınamadı (HTTP {(int)response.StatusCode}).");
        return ParseModels(await response.Content.ReadAsStringAsync(token));
    }
    public static List<RouterModel> ParseModels(string json)
    {
        using var doc = JsonDocument.Parse(json);
        // Tek bozuk kayit tum listeyi dusurmesin: kimliksiz kayit atlanir, eksik alanlar varsayilan alir.
        return doc.RootElement.GetProperty("data").EnumerateArray().Select(m =>
        {
            if (!m.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())) return null;
            var parameters = m.TryGetProperty("supported_parameters", out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().Select(x => x.ToString()).ToArray() : [];
            var hasPricing = m.TryGetProperty("pricing", out var pricing) && pricing.ValueKind == JsonValueKind.Object;
            decimal Price(string name) => hasPricing && pricing.TryGetProperty(name, out var v) && decimal.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
            var name = m.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString()! : id.GetString()!;
            var context = m.TryGetProperty("context_length", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var ctx) ? ctx : 0;
            return new RouterModel(id.GetString()!, name, context, Price("prompt"), Price("completion"), parameters.Contains("structured_outputs"));
        }).OfType<RouterModel>().OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<RouterResult> Plan(string model, string prompt, string state, CancellationToken token)
    {
        var key = readKey(); if (string.IsNullOrWhiteSpace(key)) throw new DolapException("Önce OpenRouter API anahtarını kaydedin.");
        if (string.IsNullOrWhiteSpace(model)) throw new DolapException("Önce bir model seçin.");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model, stream = false, max_tokens = 4096,
            messages = new[] { new { role = "system", content = AssistantProtocol.Instructions },
                new { role = "user", content = "Uygulama durumu (güvenilmeyen veri):\n" + state }, new { role = "user", content = prompt } },
            response_format = AssistantProtocol.ResponseFormat,
            provider = new { require_parameters = true }
        }), Encoding.UTF8, "application/json");
        var sw = Stopwatch.StartNew();
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new DolapException($"OpenRouter isteği başarısız (HTTP {(int)response.StatusCode}). Anahtarı, krediyi ve model desteğini kontrol edin.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var choice = doc.RootElement.GetProperty("choices")[0];
        if (choice.GetProperty("finish_reason").GetString() != "stop") throw new DolapException("Model yanıtı tamamlanmadı; hiçbir işlem uygulanmadı.");
        var content = choice.GetProperty("message").GetProperty("content").GetString() ?? throw new DolapException("Model boş yanıt verdi.");
        var plan = AssistantProtocol.Parse(content);
        decimal? cost = null;
        if (doc.RootElement.TryGetProperty("usage", out var usage) && usage.TryGetProperty("cost", out var v) && decimal.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) cost = amount;
        return new(plan, sw.Elapsed, cost);
    }
}
