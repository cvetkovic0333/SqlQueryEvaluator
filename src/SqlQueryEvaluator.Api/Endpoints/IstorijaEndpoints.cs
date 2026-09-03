using SqlQueryEvaluator.Core.Persistence;

namespace SqlQueryEvaluator.Api.Endpoints;

public static class IstorijaEndpoints
{
    public static void MapIstorijaEndpoints(this WebApplication app)
    {
        app.MapGet("/api/istorija", async (
            int? limit, IstorijaRepository repo, CancellationToken ct) =>
        {
            var koliko = Math.Clamp(limit ?? 50, 1, 200);
            var stavke = await repo.PoslednjiAsync(koliko, ct);

            return Results.Ok(stavke.Select(s => new
            {
                upitId = s.UpitId,
                pitanje = s.Pitanje,
                jezik = s.Jezik,
                baza = s.Baza,
                tabele = s.IzabraneTabele,
                modelId = s.ModelId,
                sql = s.GenerisaniSql,
                ocena = s.OcenaSudije,
                obrazlozenje = s.Obrazlozenje,
                izvrsen = s.Izvrsen,
                brojRedova = s.BrojRedova,
                trajanjeMs = s.TrajanjeMs,
                greska = s.Greska,
                kreirano = s.Kreirano
            }));
        });

        app.MapDelete("/api/istorija", async (IstorijaRepository repo, CancellationToken ct) =>
        {
            await repo.ObrisiSveAsync(ct);
            return Results.Ok(new { poruka = "Istorija je obrisana." });
        });
    }
}
