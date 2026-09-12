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
            throw new LlmException(SkratiGresku(sadrzaj), (int)odgovor.StatusCode,
                cekajSekundi: ProcitajRetryAfter(odgovor));

        try
        {
            using var dok = JsonDocument.Parse(sadrzaj);
            var koren = dok.RootElement;

            if (!koren.TryGetProperty("candidates", out var kandidati) || kandidati.GetArrayLength() == 0)
                throw new LlmException($"Gemini nije vratio nijedan odgovor: {SkratiGresku(sadrzaj)}");

            var kandidat = kandidati[0];

            // Model može da vrati više delova (parts) — spajaju se svi osim
            // onih označenih kao razmišljanje (thought: true), jer to nije
            // odgovor. Kada je budžet potrošen još u razmišljanju, delova
            // nema uopšte, pa se i to prihvata kao prazan tekst.
            var tekst = "";
            if (kandidat.TryGetProperty("content", out var sadrzajKandidata)
                && sadrzajKandidata.TryGetProperty("parts", out var delovi))
            {
                tekst = string.Concat(delovi.EnumerateArray()
                    .Where(d => !(d.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True))
                    .Select(d => d.TryGetProperty("text", out var t) ? t.GetString() : null));
            }

            // Gemini 3 razmišlja pre odgovora i to razmišljanje troši ISTI
            // budžet (maxOutputTokens) kao i sam odgovor. Kada ga potroši,
            // vraća MAX_TOKENS i odsečen komad teksta — nekad kraj
            // razmišljanja, nekad pola upita. Takav tekst nije odgovor modela
            // i ne sme da se meri kao da jeste.
            var presecen = kandidat.TryGetProperty("finishReason", out var razlog)
                && razlog.GetString() == "MAX_TOKENS";

            var ulazni = 0;
            var izlazni = 0;
            if (koren.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var pt)) ulazni = pt.GetInt32();
                if (usage.TryGetProperty("candidatesTokenCount", out var ctk)) izlazni = ctk.GetInt32();

                // Tokeni razmišljanja se ne vide u odgovoru, ali troše granicu
                // i naplaćuju se. Bez njih bi izgledalo da je model napisao
                // 45 tokena, a potrošio je 1200. Groq ih već uračunava.
                if (usage.TryGetProperty("thoughtsTokenCount", out var tt)) izlazni += tt.GetInt32();
            }

            return new LlmResponse(tekst.Trim(), ulazni, izlazni, sat.ElapsedMilliseconds, Opis.Id, presecen);
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
