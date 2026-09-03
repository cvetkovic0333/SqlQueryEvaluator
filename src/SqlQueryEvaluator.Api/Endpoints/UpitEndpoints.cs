using Microsoft.Extensions.Options;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Persistence;
using SqlQueryEvaluator.Core.TextToSql;

namespace SqlQueryEvaluator.Api.Endpoints;

public sealed record PrevodZahtev(
    string Pitanje,
    string? Jezik,
    string Baza,
    string[]? Tabele,
    string? ModelId);

public sealed record IzvrsiZahtev(string Sql, long? UpitId);

public static class UpitEndpoints
{
    public static void MapUpitEndpoints(this WebApplication app)
    {
        // Korak 1: pitanje -> SQL -> sanitizer -> ocena sudije.
        // Upit se NE izvršava ovde; izvršavanje je izričit, poseban korak,
        // tačno kako je traženo u opisu rada.
        app.MapPost("/api/prevedi", async (
            PrevodZahtev zahtev,
            TextToSqlService servis,
            IstorijaRepository istorija,
            IOptions<TextToSqlOptions> opcije,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(zahtev.Pitanje))
                return Results.BadRequest(new { greska = "Pitanje ne sme biti prazno." });

            if (!opcije.Value.Baze.Any(b => b.Equals(zahtev.Baza, StringComparison.OrdinalIgnoreCase)))
                return Results.BadRequest(new { greska = $"Nepoznata baza: {zahtev.Baza}" });

            var rezultat = await servis.PreveediAsync(
                zahtev.Pitanje, zahtev.Baza, zahtev.Tabele, zahtev.ModelId, oceniSudijom: true, ct);

            if (!rezultat.Uspesno)
                return Results.Problem(rezultat.Greska, statusCode: 502);

            var upitId = await istorija.DodajAsync(new IstorijaUpisa
            {
                Pitanje = zahtev.Pitanje,
                Jezik = (zahtev.Jezik ?? "sr").ToLowerInvariant() == "en" ? "en" : "sr",
                Baza = zahtev.Baza,
                IzabraneTabele = zahtev.Tabele ?? [],
                ModelId = rezultat.ModelId,
                GenerisaniSql = rezultat.Bezbedan ? rezultat.Sql : rezultat.SirovOdgovor,
                OcenaSudije = rezultat.Ocena?.Ocena is > 0 ? rezultat.Ocena.Ocena : null,
                Obrazlozenje = rezultat.Ocena?.Obrazlozenje,
                Izvrsen = false,
                Greska = rezultat.RazlogOdbijanja
            }, ct);

            var ocena = rezultat.Ocena?.Ocena ?? 0;

            return Results.Ok(new
            {
                upitId,
                sql = rezultat.Sql,
                sirovOdgovor = rezultat.SirovOdgovor,
                modelId = rezultat.ModelId,
                trajanjeMs = rezultat.TrajanjeMs,
                ulazniTokeni = rezultat.UlazniTokeni,
                izlazniTokeni = rezultat.IzlazniTokeni,
                bezbedan = rezultat.Bezbedan,
                razlogOdbijanja = rezultat.RazlogOdbijanja,
                ocena = rezultat.Ocena is null ? null : new
                {
                    vrednost = rezultat.Ocena.Ocena,
                    tacan = rezultat.Ocena.Tacan,
                    obrazlozenje = rezultat.Ocena.Obrazlozenje,
                    modelSudije = rezultat.Ocena.ModelSudije
                },
                // Upit ocenjen kao dobar sme odmah na izvršenje; ispod praga
                // interfejs traži izričitu potvrdu.
                smeOdmahDaSeIzvrsi = rezultat.Bezbedan && ocena >= opcije.Value.MinimalnaOcenaZaIzvrsavanje
            });
        });

        // Korak 2: izvršavanje. Sanitizer se pušta ponovo — telo zahteva
        // dolazi od klijenta i ne sme se verovati da je isto ono što je
        // prošlo proveru u koraku 1.
        app.MapPost("/api/izvrsi", async (
            IzvrsiZahtev zahtev,
            QueryExecutor izvrsilac,
            IstorijaRepository istorija,
            CancellationToken ct) =>
        {
            var provera = SqlSanitizer.Proveri(zahtev.Sql);
            if (!provera.Prihvacen)
                return Results.BadRequest(new { greska = provera.Razlog, odbijenoNaProveri = true });

            var rezultat = await izvrsilac.IzvrsiAsync(provera.Sql, ct);

            if (zahtev.UpitId is > 0)
            {
                await istorija.OznaciIzvrsenAsync(
                    zahtev.UpitId.Value,
                    rezultat.BrojRedova,
                    (int)rezultat.TrajanjeMs,
                    rezultat.Greska,
                    ct);
            }

            if (!rezultat.Uspesno)
                return Results.BadRequest(new { greska = rezultat.Greska, sqlState = rezultat.SqlState });

            return Results.Ok(new
            {
                kolone = rezultat.Kolone,
                redovi = rezultat.Redovi,
                brojRedova = rezultat.BrojRedova,
                odsecen = rezultat.Odsecen,
                trajanjeMs = rezultat.TrajanjeMs
            });
        });
    }
}
