using Microsoft.Extensions.Options;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.TextToSql;

namespace SqlQueryEvaluator.Api.Endpoints;

public static class SemaEndpoints
{
    public static void MapSemaEndpoints(this WebApplication app)
    {
        app.MapGet("/api/baze", (IOptions<TextToSqlOptions> opcije) =>
            Results.Ok(opcije.Value.Baze.Select(b => new
            {
                naziv = b,
                prikaznoIme = PrikaznoIme(b)
            })));

        app.MapGet("/api/sema/{baza}", async (
            string baza,
            SchemaIntrospector introspektor,
            IOptions<TextToSqlOptions> opcije,
            CancellationToken ct) =>
        {
            if (!DozvoljenaBaza(baza, opcije.Value))
                return Results.BadRequest(new { greska = $"Nepoznata baza: {baza}" });

            var sema = await introspektor.UcitajAsync(baza, ct);
            return Results.Ok(new
            {
                naziv = sema.Naziv,
                prikaznoIme = PrikaznoIme(sema.Naziv),
                opis = sema.Opis,
                ukupnoRedova = sema.Tabele.Sum(t => t.BrojRedova),
                tabele = sema.Tabele.Select(t => new
                {
                    naziv = t.Naziv,
                    opis = t.Opis,
                    brojRedova = t.BrojRedova,
                    kolone = t.Kolone.Select(k => new
                    {
                        naziv = k.Naziv,
                        tip = k.Tip,
                        mozeBitiNull = k.MozeBitiNull,
                        primarniKljuc = k.PrimarniKljuc,
                        opis = k.Opis
                    }),
                    straniKljucevi = t.StraniKljucevi.Select(f => new
                    {
                        kolona = f.Kolona,
                        ciljnaTabela = f.CiljnaTabela,
                        ciljnaKolona = f.CiljnaKolona
                    })
                })
            });
        });

        app.MapGet("/api/sema/{baza}/{tabela}/pregled", async (
            string baza,
            string tabela,
            int? limit,
            int? offset,
            SchemaIntrospector introspektor,
            QueryExecutor izvrsilac,
            IOptions<TextToSqlOptions> opcije,
            CancellationToken ct) =>
        {
            if (!DozvoljenaBaza(baza, opcije.Value))
                return Results.BadRequest(new { greska = $"Nepoznata baza: {baza}" });

            var sema = await introspektor.UcitajAsync(baza, ct);
            var nadjena = sema.Tabele.FirstOrDefault(t =>
                t.Naziv.Equals(tabela, StringComparison.OrdinalIgnoreCase));

            if (nadjena is null)
                return Results.NotFound(new { greska = $"Tabela '{tabela}' ne postoji u bazi '{baza}'." });

            var koliko = Math.Clamp(limit ?? 50, 1, 500);
            var preskoci = Math.Max(offset ?? 0, 0);

            var redosled = nadjena.Kolone.FirstOrDefault(k => k.PrimarniKljuc)?.Naziv
                           ?? nadjena.Kolone.First().Naziv;

            var rezultat = await izvrsilac.IzvrsiAsync(
                $"SELECT * FROM {sema.Naziv}.{nadjena.Naziv} ORDER BY {redosled} OFFSET {preskoci}",
                koliko, ct);

            if (!rezultat.Uspesno)
                return Results.Problem(rezultat.Greska);

            return Results.Ok(new
            {
                tabela = nadjena.Naziv,
                opis = nadjena.Opis,
                ukupnoRedova = nadjena.BrojRedova,
                offset = preskoci,
                kolone = rezultat.Kolone,
                redovi = rezultat.Redovi,
                imaJos = preskoci + rezultat.BrojRedova < nadjena.BrojRedova,
                trajanjeMs = rezultat.TrajanjeMs
            });
        });

        app.MapPost("/api/sema/{baza}/osvezi", (
            string baza, SchemaIntrospector introspektor) =>
        {
            introspektor.OcistiKes(baza);
            return Results.Ok(new { poruka = "Keš šeme je očišćen." });
        });
    }

    private static bool DozvoljenaBaza(string baza, TextToSqlOptions opcije) =>
        opcije.Baze.Any(b => b.Equals(baza, StringComparison.OrdinalIgnoreCase));

    private static string PrikaznoIme(string sema) => sema switch
    {
        "prodavnica" => "Online prodavnica",
        "fakultet" => "Fakultet",
        _ => sema
    };
}
