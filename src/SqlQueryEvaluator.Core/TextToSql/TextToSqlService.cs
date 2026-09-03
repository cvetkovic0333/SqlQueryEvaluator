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
    string? Greska)
{
    public static PrevodRezultat Neuspeh(string greska, string modelId) =>
        new(false, "", "", modelId, 0, 0, 0, false, null, null, greska);
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
            return PrevodRezultat.Neuspeh(ex.Message, model);
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
}
