using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SqlQueryEvaluator.Core.Llm;
using SqlQueryEvaluator.Core.Models;
using SqlQueryEvaluator.Core.TextToSql;

namespace SqlQueryEvaluator.Core.Evaluation;

public sealed record OcenaSudije(int Ocena, bool Tacan, string Obrazlozenje, string ModelSudije)
{
    public static OcenaSudije Greska(string poruka, string model) =>
        new(0, false, $"Sudija nije uspeo da oceni upit: {poruka}", model);
}

/// <summary>
/// LLM-as-a-Judge — model ocenjuje kvalitet generisanog SQL-a.
///
/// Radi u dva režima:
///   - u benchmarku ima i gold SQL, pa poredi dva upita;
///   - u aplikaciji gold SQL ne postoji, pa procenjuje da li upit odgovara
///     na postavljeno pitanje nad datom šemom.
///
/// Sudija treba da bude model RAZLIČIT od generatora. Kada model ocenjuje
/// sopstveni izlaz javlja se self-preference bias — sklon je da sebe oceni
/// bolje. To je ograničenje metode i u radu se navodi eksplicitno.
/// </summary>
public sealed class LlmJudge(ILlmProviderFactory fabrika)
{
    private const string SistemskiPrompt = """
        You are a strict PostgreSQL reviewer. You evaluate whether a generated SQL
        query correctly answers a natural-language question about a given schema.

        Respond with ONLY a JSON object, no markdown, in exactly this shape:
        {"ocena": <integer 1-5>, "tacan": <true|false>, "obrazlozenje": "<one or two sentences in Serbian>"}

        Scale:
          5 = fully correct; returns exactly what the question asks
          4 = correct result, minor stylistic issues (extra column, odd alias)
          3 = mostly right but flawed (missing filter, wrong sorting, wrong LIMIT)
          2 = runs, but answers a different question
          1 = wrong or invalid SQL

        "tacan" must be true only when the query genuinely answers the question
        (that is, when ocena is 4 or 5).

        Be strict about: missing WHERE filters, wrong JOIN direction, aggregation
        over the wrong column, forgotten GROUP BY, and NULL handling.
        """;

    public async Task<OcenaSudije> OceniAsync(
        string modelSudijeId,
        DatabaseSchema sema,
        string pitanje,
        string generisaniSql,
        string? goldSql = null,
        string? greskaIzvrsavanja = null,
        CancellationToken ct = default)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(PromptBuilder.OpisSeme(sema));
        prompt.AppendLine();
        prompt.Append("Question: ").AppendLine(pitanje.Trim());
        prompt.AppendLine();
        prompt.AppendLine("Generated SQL:");
        prompt.AppendLine(generisaniSql.Trim());

        if (!string.IsNullOrWhiteSpace(goldSql))
        {
            prompt.AppendLine();
            prompt.AppendLine("Reference (gold) SQL written by a human expert:");
            prompt.AppendLine(goldSql.Trim());
            prompt.AppendLine();
            prompt.AppendLine("The generated SQL does not have to be textually identical to the "
                            + "reference — a different but equivalent formulation is fully correct.");
        }

        if (!string.IsNullOrWhiteSpace(greskaIzvrsavanja))
        {
            prompt.AppendLine();
            prompt.Append("Note: executing the generated SQL failed with: ").AppendLine(greskaIzvrsavanja);
        }

        prompt.AppendLine();
        prompt.Append("JSON:");

        try
        {
            var provajder = fabrika.Kreiraj(modelSudijeId);
            var odgovor = await provajder.CompleteAsync(
                new LlmRequest(SistemskiPrompt, prompt.ToString(), Temperature: 0, MaxTokens: 400, JsonMode: true),
                ct);

            return Rasclani(odgovor.Text, modelSudijeId);
        }
        catch (LlmException ex)
        {
            return OcenaSudije.Greska(ex.Message, modelSudijeId);
        }
    }

    /// <summary>
    /// Modeli i pored JSON režima umeju da vrate tekst oko objekta ili da ga
    /// obmotaju u ```json ogradu, pa se prvi JSON objekat izvlači regularnim
    /// izrazom pre parsiranja.
    /// </summary>
    internal static OcenaSudije Rasclani(string odgovor, string modelSudije)
    {
        var podudaranje = Regex.Match(odgovor, @"\{.*\}", RegexOptions.Singleline);
        if (!podudaranje.Success)
            return OcenaSudije.Greska($"odgovor nije sadržao JSON ({Skrati(odgovor)})", modelSudije);

        try
        {
            using var dok = JsonDocument.Parse(podudaranje.Value);
            var koren = dok.RootElement;

            var ocena = koren.TryGetProperty("ocena", out var o) ? CitajCeoBroj(o) : 0;
            ocena = Math.Clamp(ocena, 1, 5);

            var tacan = koren.TryGetProperty("tacan", out var t) && CitajLogicku(t);

            var obrazlozenje = koren.TryGetProperty("obrazlozenje", out var ob)
                ? ob.GetString() ?? ""
                : "";

            // Ako model kaže "tacan: true" a da ocenu ispod 4, veruje se oceni —
            // ona je konkretnija i lakše se poredi sa execution accuracy.
            if (ocena < 4) tacan = false;

            return new OcenaSudije(ocena, tacan, obrazlozenje.Trim(), modelSudije);
        }
        catch (JsonException ex)
        {
            return OcenaSudije.Greska($"neispravan JSON ({ex.Message})", modelSudije);
        }
    }

    private static int CitajCeoBroj(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.TryGetInt32(out var i) ? i : (int)Math.Round(e.GetDouble()),
        JsonValueKind.String => int.TryParse(e.GetString(), out var i) ? i : 0,
        _ => 0
    };

    private static bool CitajLogicku(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(e.GetString(), out var b) && b,
        _ => false
    };

    private static string Skrati(string s) => s.Length <= 120 ? s : s[..120] + "…";
}
