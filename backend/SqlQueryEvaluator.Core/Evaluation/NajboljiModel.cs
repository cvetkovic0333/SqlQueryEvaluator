using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Llm;
using SqlQueryEvaluator.Core.Persistence;

namespace SqlQueryEvaluator.Core.Evaluation;

public sealed class NajboljiModel(
    BenchmarkRepository repo,
    ILlmProviderFactory fabrika,
    IOptions<TextToSqlOptions> opcije,
    IMemoryCache kes)
{
    private const string KljucKesa = "najbolji-model";

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
            return rezerva;
        }
    }
}
