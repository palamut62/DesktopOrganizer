using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Dolap.Core;

public sealed record RouterModel(string Id, string Name, int Context, decimal InputPrice, decimal OutputPrice, bool Structured)
{
    public string Label => $"{Name} ({Id})";
    public string Details => $"Bağlam: {Context:N0} · 1M token giriş: ${InputPrice * 1000000:0.######} / çıkış: ${OutputPrice * 1000000:0.######} · JSON şeması: {(Structured ? "var" : "yok")}";
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
        return doc.RootElement.GetProperty("data").EnumerateArray().Select(m =>
        {
            var parameters = m.TryGetProperty("supported_parameters", out var p) ? p.EnumerateArray().Select(x => x.GetString()).ToArray() : [];
            var pricing = m.GetProperty("pricing");
            decimal Price(string name) => pricing.TryGetProperty(name, out var v) && decimal.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
            return new RouterModel(m.GetProperty("id").GetString()!, m.GetProperty("name").GetString()!, m.GetProperty("context_length").GetInt32(), Price("prompt"), Price("completion"), parameters.Contains("structured_outputs"));
        }).OrderBy(m => m.Name).ToList();
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
