using Microsoft.Extensions.Options;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Evaluation;
using SqlQueryEvaluator.Core.Llm;

namespace SqlQueryEvaluator.Core.TextToSql;

public sealed record PrevodRezultat(
    bool Uspesno,
    string Sql,
    string SirovOdgovor,
    string ModelId,
    long TrajanjeMs,
    int UlazniTokeni,
    int IzlazniTokeni,
    bool Bezbedan,
    string? RazlogOdbijanja,
    OcenaSudije? Ocena,
    string? Greska,
    bool KvotaIscrpljena = false)
{
    public static PrevodRezultat Neuspeh(string greska, string modelId, bool kvotaIscrpljena = false) =>
        new(false, "", "", modelId, 0, 0, 0, false, null, null, greska, kvotaIscrpljena);
}

/// <summary>
/// Spaja ceo tok koji je tražen u opisu rada:
/// pitanje → model → SQL → sanitizer → sudija oceni upit.
///
/// Izvršavanje je namerno ODVOJEN korak (<see cref="QueryExecutor"/>) — upit
/// se šalje na izvršenje tek pošto je ocenjen, kao izričita akcija.
/// </summary>
public sealed class TextToSqlService(
    ILlmProviderFactory fabrika,
    SchemaIntrospector introspektor,
    LlmJudge sudija,
    IOptions<TextToSqlOptions> opcije)
{
    private readonly TextToSqlOptions _opcije = opcije.Value;

    public async Task<PrevodRezultat> PreveediAsync(
        string pitanje,
        string baza,
        IReadOnlyCollection<string>? izabraneTabele = null,
        string? modelId = null,
        bool oceniSudijom = true,
        CancellationToken ct = default)
    {
        var model = string.IsNullOrWhiteSpace(modelId) ? _opcije.DefaultModelId : modelId;
        if (string.IsNullOrWhiteSpace(model))
            return PrevodRezultat.Neuspeh(
                "Nije podešen model za generisanje SQL-a (TextToSql:DefaultModelId).", "");

        var sema = await introspektor.UcitajAsync(baza, ct);

        string sirov;
        long trajanje;
        int ulazni, izlazni;
        try
        {
            var provajder = fabrika.Kreiraj(model);
            var odgovor = await provajder.CompleteAsync(new LlmRequest(
                PromptBuilder.SistemskiPrompt,
                PromptBuilder.KorisnickiPrompt(sema, pitanje, izabraneTabele),
                Temperature: provajder.Opis.Temperature,
                MaxTokens: provajder.Opis.MaxTokens), ct);

            sirov = odgovor.Text;
            trajanje = odgovor.TrajanjeMs;
            ulazni = odgovor.UlazniTokeni;
            izlazni = odgovor.IzlazniTokeni;
        }
        catch (LlmException ex)
        {
            // Sirova poruka provajdera je stranica JSON-a i korisniku ne znači
            // ništa. Prevodi se u rečenicu koja kaže šta da uradi.
            return PrevodRezultat.Neuspeh(ObjasniGresku(ex, model), model, ex.RateLimit);
        }

        var provera = SqlSanitizer.Proveri(sirov);
        if (!provera.Prihvacen)
        {
            return new PrevodRezultat(true, "", sirov, model, trajanje, ulazni, izlazni,
                false, provera.Razlog, null, null);
        }

        OcenaSudije? ocena = null;
        if (oceniSudijom && !string.IsNullOrWhiteSpace(_opcije.JudgeModelId))
        {
            ocena = await sudija.OceniAsync(_opcije.JudgeModelId, sema, pitanje, provera.Sql, ct: ct);
        }

        return new PrevodRezultat(true, provera.Sql, sirov, model, trajanje, ulazni, izlazni,
            true, null, ocena, null);
    }

    /// <summary>
    /// Poruke provajdera su tehničke i na engleskom. Ovde se svode na
    /// rečenicu koja kaže šta se desilo i šta korisnik može da uradi —
    /// najčešće da izabere drugi model iz padajućeg menija.
    /// </summary>
    private string ObjasniGresku(LlmException ex, string modelId)
    {
        var naziv = fabrika.DostupniModeli.FirstOrDefault(m => m.Id == modelId)?.PrikaznoIme ?? modelId;

        if (ex.RateLimit)
        {
            var kada = ex.CekajSekundi is { } s and > 0
                ? s >= 120 ? $" Pokušaj ponovo za {s / 60} min." : $" Pokušaj ponovo za {s} s."
                : "";

            return $"Model „{naziv}” je iscrpeo kvotu besplatnog naloga.{kada} " +
                   "Izaberi drugi model iz padajućeg menija — ostali imaju zasebne kvote.";
        }

        if (ex.StatusKod is 401 or 403)
            return $"API ključ za model „{naziv}” nije prihvaćen. Proveri ga u appsettings.Development.json.";

        if (ex.StatusKod == 404)
            return $"Model „{naziv}” više ne postoji kod provajdera. Provajderi povlače modele — " +
                   "proveri tačan naziv sa: dotnet run --project backend/SqlQueryEvaluator.Benchmark -- --dry-run";

        return $"Model „{naziv}” nije odgovorio: {ex.Message}";
    }
}
