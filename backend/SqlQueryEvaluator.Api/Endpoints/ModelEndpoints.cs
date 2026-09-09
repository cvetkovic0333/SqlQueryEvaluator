using Microsoft.Extensions.Options;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Evaluation;
using SqlQueryEvaluator.Core.Llm;

namespace SqlQueryEvaluator.Api.Endpoints;

public static class ModelEndpoints
{
    public static void MapModelEndpoints(this WebApplication app)
    {
        // Lista modela iz registra, sa podatkom da li im je ključ podešen.
        // Interfejs na osnovu toga jasno kaže šta fali, umesto da poziv
        // pukne tek kada korisnik pritisne "Prevedi".
        app.MapGet("/api/modeli", async (
            ILlmProviderFactory fabrika,
            NajboljiModel najbolji,
            IOptions<TextToSqlOptions> opcije,
            CancellationToken ct) =>
        {
            var o = opcije.Value;
            var podrazumevani = await najbolji.OdrediAsync(ct);
            var modeli = fabrika.DostupniModeli.Select(m => new
            {
                id = m.Id,
                naziv = m.PrikaznoIme,
                provajder = m.Provider,
                model = m.Model,
                imaKljuc = fabrika.ImaKljuc(m.Id),
                podrazumevani = m.Id == podrazumevani,
                sudija = m.Id == o.JudgeModelId
            }).ToList();

            return Results.Ok(new
            {
                modeli,
                podrazumevaniModel = podrazumevani,
                modelSudije = o.JudgeModelId,
                minimalnaOcena = o.MinimalnaOcenaZaIzvrsavanje,
                spremno = modeli.Any(m => m.imaKljuc)
            });
        });
    }
}
