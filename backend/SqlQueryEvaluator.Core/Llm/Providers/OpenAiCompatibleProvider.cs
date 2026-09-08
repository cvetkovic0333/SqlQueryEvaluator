using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using SqlQueryEvaluator.Core.Configuration;

namespace SqlQueryEvaluator.Core.Llm.Providers;

/// <summary>
/// Jedna klasa pokriva Groq, OpenRouter i Mistral — sva tri izlažu isti
/// oblik: POST {BaseUrl}/chat/completions sa "Authorization: Bearer".
/// Razlike su samo u BaseUrl-u, nazivu modela i ključu, a to sve dolazi
/// iz <see cref="ModelDescriptor"/>-a, dakle iz appsettings.json.
/// </summary>
public sealed class OpenAiCompatibleProvider(
    ModelDescriptor opis,
    HttpClient http,
    string apiKljuc) : ILlmProvider
{
    public ModelDescriptor Opis { get; } = opis;

    public async Task<LlmResponse> CompleteAsync(LlmRequest zahtev, CancellationToken ct = default)
    {
        var telo = new Dictionary<string, object?>
        {
            ["model"] = Opis.Model,
            ["temperature"] = zahtev.Temperature,
            ["max_tokens"] = zahtev.MaxTokens,
            ["messages"] = new object[]
            {
                new { role = "system", content = zahtev.SystemPrompt },
                new { role = "user", content = zahtev.UserPrompt }
            }
        };

        if (zahtev.JsonMode)
            telo["response_format"] = new { type = "json_object" };

        using var poruka = new HttpRequestMessage(HttpMethod.Post, $"{Opis.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(telo)
        };
        poruka.Headers.Add("Authorization", $"Bearer {apiKljuc}");

        // OpenRouter traži ova zaglavlja da bi zahtev pripisao aplikaciji.
        if (Opis.BaseUrl.Contains("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            poruka.Headers.Add("HTTP-Referer", "https://github.com/cvetkovic0333/SqlQueryEvaluator");
            poruka.Headers.Add("X-Title", "SqlQueryEvaluator");
        }

        var sat = Stopwatch.StartNew();
        HttpResponseMessage odgovor;
        try
        {
            odgovor = await http.SendAsync(poruka, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new LlmException($"Poziv ka {Opis.Provider} nije uspeo: {ex.Message}", null, ex);
        }
        sat.Stop();

        var sadrzaj = await odgovor.Content.ReadAsStringAsync(ct);
        if (!odgovor.IsSuccessStatusCode)
            throw new LlmException(SkratiGresku(sadrzaj), (int)odgovor.StatusCode,
                cekajSekundi: ProcitajRetryAfter(odgovor));

        try
        {
            using var dok = JsonDocument.Parse(sadrzaj);
            var koren = dok.RootElement;

            var tekst = koren.GetProperty("choices")[0]
                             .GetProperty("message")
                             .GetProperty("content")
                             .GetString() ?? "";

            var ulazni = 0;
            var izlazni = 0;
            if (koren.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt)) ulazni = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ctk)) izlazni = ctk.GetInt32();
            }

            return new LlmResponse(tekst.Trim(), ulazni, izlazni, sat.ElapsedMilliseconds, Opis.Id);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new LlmException($"Neočekivan oblik odgovora od {Opis.Provider}: {SkratiGresku(sadrzaj)}", null, ex);
        }
    }


    /// <summary>
    /// Provajder u zaglavlju Retry-After kaže koliko treba čekati. Bez toga
    /// se pogađa, a svako promašeno pogađanje troši jedan zahtev iz dnevne
    /// kvote i ništa ne dobija.
    /// </summary>
    private static int? ProcitajRetryAfter(HttpResponseMessage odgovor)
    {
        if (odgovor.Headers.RetryAfter?.Delta is { } razmak)
            return (int)Math.Ceiling(razmak.TotalSeconds);

        if (odgovor.Headers.TryGetValues("retry-after", out var vrednosti)
            && int.TryParse(vrednosti.FirstOrDefault(), out var sekundi))
            return sekundi;

        return null;
    }

    private static string SkratiGresku(string telo) =>
        telo.Length <= 400 ? telo : telo[..400] + "…";
}
