using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using SqlQueryEvaluator.Core.Configuration;

namespace SqlQueryEvaluator.Core.Llm.Providers;

/// <summary>
/// Google Gemini je jedini provajder u registru koji ne prati OpenAI oblik:
/// telo je contents[].parts[].text, a sistemski prompt ide u posebno polje
/// systemInstruction. Zato ima svoju klasu.
/// </summary>
public sealed class GeminiProvider(
    ModelDescriptor opis,
    HttpClient http,
    string apiKljuc) : ILlmProvider
{
    private const string Osnova = "https://generativelanguage.googleapis.com/v1beta";

    public ModelDescriptor Opis { get; } = opis;

    public async Task<LlmResponse> CompleteAsync(LlmRequest zahtev, CancellationToken ct = default)
    {
        var generationConfig = new Dictionary<string, object?>
        {
            ["temperature"] = zahtev.Temperature,
            ["maxOutputTokens"] = zahtev.MaxTokens
        };

        if (zahtev.JsonMode)
            generationConfig["responseMimeType"] = "application/json";

        var telo = new
        {
            systemInstruction = new { parts = new[] { new { text = zahtev.SystemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = zahtev.UserPrompt } } }
            },
            generationConfig
        };

        var url = $"{Osnova}/models/{Opis.Model}:generateContent";
        using var poruka = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(telo)
        };
        poruka.Headers.Add("x-goog-api-key", apiKljuc);

        var sat = Stopwatch.StartNew();
        HttpResponseMessage odgovor;
        try
        {
            odgovor = await http.SendAsync(poruka, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new LlmException($"Poziv ka Gemini nije uspeo: {ex.Message}", null, ex);
        }
        sat.Stop();

        var sadrzaj = await odgovor.Content.ReadAsStringAsync(ct);
        if (!odgovor.IsSuccessStatusCode)
            throw new LlmException(SkratiGresku(sadrzaj), (int)odgovor.StatusCode);

        try
        {
            using var dok = JsonDocument.Parse(sadrzaj);
            var koren = dok.RootElement;

            if (!koren.TryGetProperty("candidates", out var kandidati) || kandidati.GetArrayLength() == 0)
                throw new LlmException($"Gemini nije vratio nijedan odgovor: {SkratiGresku(sadrzaj)}");

            // Model može da vrati više delova (parts) — spajaju se svi.
            var delovi = kandidati[0].GetProperty("content").GetProperty("parts");
            var tekst = string.Concat(delovi.EnumerateArray()
                .Select(d => d.TryGetProperty("text", out var t) ? t.GetString() : null));

            var ulazni = 0;
            var izlazni = 0;
            if (koren.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var pt)) ulazni = pt.GetInt32();
                if (usage.TryGetProperty("candidatesTokenCount", out var ctk)) izlazni = ctk.GetInt32();
            }

            return new LlmResponse((tekst ?? "").Trim(), ulazni, izlazni, sat.ElapsedMilliseconds, Opis.Id);
        }
        catch (LlmException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new LlmException($"Neočekivan oblik odgovora od Gemini: {SkratiGresku(sadrzaj)}", null, ex);
        }
    }

    private static string SkratiGresku(string telo) =>
        telo.Length <= 400 ? telo : telo[..400] + "…";
}
