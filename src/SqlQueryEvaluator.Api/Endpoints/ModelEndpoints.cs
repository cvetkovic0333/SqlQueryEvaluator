using Microsoft.Extensions.Options;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Llm;

namespace SqlQueryEvaluator.Api.Endpoints;

public static class ModelEndpoints
{
    public static void MapModelEndpoints(this WebApplication app)
    {
        // Lista modela iz registra, sa podatkom da li im je ključ podešen.
        // Interfejs na osnovu toga jasno kaže šta fali, umesto da poziv
        // pukne tek kada korisnik pritisne "Prevedi".
        app.MapGet("/api/modeli", (
            ILlmProviderFactory fabrika,
            IOptions<TextToSqlOptions> opcije) =>
        {
            var o = opcije.Value;
            var modeli = fabrika.DostupniModeli.Select(m => new
            {
                id = m.Id,
                naziv = m.PrikaznoIme,
                provajder = m.Provider,
                model = m.Model,
                imaKljuc = fabrika.ImaKljuc(m.Id),
                podrazumevani = m.Id == o.DefaultModelId,
                sudija = m.Id == o.JudgeModelId
            }).ToList();

            return Results.Ok(new
            {
                modeli,
                podrazumevaniModel = o.DefaultModelId,
                modelSudije = o.JudgeModelId,
                minimalnaOcena = o.MinimalnaOcenaZaIzvrsavanje,
                spremno = modeli.Any(m => m.imaKljuc)
            });
        });
    }
}
