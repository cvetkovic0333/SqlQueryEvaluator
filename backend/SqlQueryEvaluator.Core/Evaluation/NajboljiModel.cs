using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Llm;
using SqlQueryEvaluator.Core.Persistence;

namespace SqlQueryEvaluator.Core.Evaluation;

/// <summary>
/// Određuje koji model aplikacija koristi kada joj se ne kaže izričito.
///
/// Vrednost se NE upisuje ručno u konfiguraciju nego se računa iz poslednjeg
/// merenja: pobednik je model sa najvećom tačnošću, a pri izjednačenju brži.
/// Ručno upisana vrednost zastari čim stigne novo merenje — ovako izbor uvek
/// prati podatke.
///
/// TextToSql:DefaultModelId ostaje kao rezerva, za slučaj da merenja još nema.
/// </summary>
public sealed class NajboljiModel(
    BenchmarkRepository repo,
    ILlmProviderFactory fabrika,
    IOptions<TextToSqlOptions> opcije,
    IMemoryCache kes)
{
    private const string KljucKesa = "najbolji-model";

    // Kratko keširanje: merenje se menja retko, a poziv bi inače išao u bazu
    // pri svakom prevođenju.
    private static readonly TimeSpan TrajanjeKesa = TimeSpan.FromMinutes(2);

    public async Task<string> OdrediAsync(CancellationToken ct = default)
    {
        if (kes.TryGetValue<string>(KljucKesa, out var iz) && !string.IsNullOrEmpty(iz))
            return iz;

        var izabran = await IzracunajAsync(ct);
        kes.Set(KljucKesa, izabran, TrajanjeKesa);
        return izabran;
    }

    public void Zaboravi() => kes.Remove(KljucKesa);

    private async Task<string> IzracunajAsync(CancellationToken ct)
    {
        var rezerva = opcije.Value.DefaultModelId;

        try
        {
            var pokretanje = await repo.PoslednjePokretanjeAsync(ct);
            if (pokretanje is null) return rezerva;

            var redovi = await repo.RezultatiAsync(pokretanje.PokretanjeId, ct);
            if (redovi.Count == 0) return rezerva;

            // Model bez podešenog ključa ne može da odgovori, pa ma koliko
            // dobro prošao na merenju ne dolazi u obzir kao podrazumevani.
            var kandidat = MetricsCalculator.PoModelu(redovi)
                .Where(p => fabrika.ImaKljuc(p.Key))
                .OrderByDescending(p => p.Value.ExecutionAccuracy)
                .ThenBy(p => p.Value.ProsecnoTrajanjeMs)
                .Select(p => p.Key)
                .FirstOrDefault();

            return kandidat ?? rezerva;
        }
        catch
        {
            // Baza nedostupna nije razlog da prevođenje stane.
            return rezerva;
        }
    }
}
