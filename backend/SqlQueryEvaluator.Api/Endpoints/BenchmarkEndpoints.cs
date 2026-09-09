using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Evaluation;
using SqlQueryEvaluator.Core.Llm;
using SqlQueryEvaluator.Core.Persistence;

namespace SqlQueryEvaluator.Api.Endpoints;

public static class BenchmarkEndpoints
{
    public static void MapBenchmarkEndpoints(this WebApplication app)
    {
        // Sve što dashboard crta dolazi odavde, iz stvarnih rezultata testa.
        // Ako test još nije pokrenut, vraća se prazno — interfejs tada
        // prikazuje uputstvo, a ne izmišljene brojeve.
        app.MapGet("/api/benchmark/pregled", async (
            int? pokretanje,
            BenchmarkRepository repo,
            ILlmProviderFactory fabrika,
            NajboljiModel najbolji,
            IOptions<TextToSqlOptions> opcije,
            CancellationToken ct) =>
        {
            var pokretanja = await repo.SvaPokretanjaAsync(ct);

            // Bez ovoga bi se rezultati svih pokretanja sabrali u jedan
            // prosek — probni run od tri zadatka bi krivio brojeve punog
            // testa. Podrazumevano se prikazuje poslednje pokretanje.
            var izabrano = pokretanje ?? pokretanja.FirstOrDefault()?.PokretanjeId;
            var redovi = izabrano is null
                ? []
                : await repo.RezultatiAsync(izabrano, ct);

            if (redovi.Count == 0)
            {
                return Results.Ok(new
                {
                    imaPodataka = false,
                    poruka = "Benchmark još nije pokrenut. Pokreni: dotnet run --project src/SqlQueryEvaluator.Benchmark",
                    izabranoPokretanje = izabrano,
                    pokretanja
                });
            }

            var nazivi = fabrika.DostupniModeli.ToDictionary(m => m.Id, m => m.PrikaznoIme);
            string Naziv(string id) => nazivi.GetValueOrDefault(id, id);

            var poModelu = MetricsCalculator.PoModelu(redovi);

            // Pobednik: najveća execution accuracy, a pri izjednačenju brži model.
            var pobednik = poModelu
                .OrderByDescending(p => p.Value.ExecutionAccuracy)
                .ThenBy(p => p.Value.ProsecnoTrajanjeMs)
                .Select(p => p.Key)
                .FirstOrDefault();

            var tezine = new[] { "lak", "srednji", "tezak" }
                .Where(t => redovi.Any(r => r.Tezina == t))
                .ToArray();

            // Prikazuju se samo jezici koji su STVARNO mereni. Ako se test
            // pusti samo na srpskom, engleski bi inače izašao kao 0% i
            // izgledalo bi da modeli na engleskom potpuno padaju — a nisu
            // ni pitani.
            var jezici = new[] { "sr", "en" }
                .Where(j => redovi.Any(r => r.Jezik == j))
                .ToArray();

            return Results.Ok(new
            {
                imaPodataka = true,
                pokretanja,
                izabranoPokretanje = izabrano,
                pobednik,
                pobednikNaziv = pobednik is null ? null : Naziv(pobednik),
                podesenModel = await najbolji.OdrediAsync(ct),
                ukupnoPoziva = redovi.Count,

                modeli = poModelu.OrderByDescending(p => p.Value.ExecutionAccuracy).Select(p => new
                {
                    modelId = p.Key,
                    naziv = Naziv(p.Key),
                    broj = p.Value.Broj,
                    validSql = p.Value.ValidSqlRate,
                    tacnost = p.Value.ExecutionAccuracy,
                    ocenaSudije = p.Value.ProsecnaOcenaSudije,
                    trajanjeMs = p.Value.ProsecnoTrajanjeMs,
                    tokeni = p.Value.ProsecnoTokena
                }),

                // Tačnost po težini zadatka — pokazuje koliko model pada
                // kada zadatak postane složeniji.
                poTezini = poModelu.Keys.OrderBy(k => k).Select(model => new
                {
                    modelId = model,
                    naziv = Naziv(model),
                    vrednosti = tezine.Select(t => MetricsCalculator
                        .ZaGrupu(t, redovi.Where(r => r.ModelId == model && r.Tezina == t).ToList())
                        .ExecutionAccuracy)
                }),
                tezine,

                // Srpski vs engleski — direktna provera koliko model gubi
                // kada pitanje nije na engleskom.
                poJeziku = poModelu.Keys.OrderBy(k => k).Select(model => new
                {
                    modelId = model,
                    naziv = Naziv(model),
                    vrednosti = jezici.Select(j => MetricsCalculator
                        .ZaGrupu(j, redovi.Where(r => r.ModelId == model && r.Jezik == j).ToList())
                        .ExecutionAccuracy)
                }),
                jezici,

                // Slaganje sudije sa objektivnom merom — koliko je
                // LLM-as-a-Judge uopšte pouzdan.
                slaganjeSudije = SlaganjeDto(MetricsCalculator.IzracunajSlaganje(redovi)),
                slaganjePoModelu = poModelu.Keys.OrderBy(k => k).Select(model =>
                {
                    var s = MetricsCalculator.IzracunajSlaganje(redovi.Where(r => r.ModelId == model));
                    return new { modelId = model, naziv = Naziv(model), slaganje = SlaganjeDto(s) };
                })
            });
        });

        // Izvoz tabele metrika u PDF — prilog za pisani deo rada.
        app.MapGet("/api/benchmark/izvoz.pdf", async (
            int? pokretanje,
            BenchmarkRepository repo,
            ILlmProviderFactory fabrika,
            NajboljiModel najbolji,
            CancellationToken ct) =>
        {
            var pokretanja = await repo.SvaPokretanjaAsync(ct);
            var izabrano = pokretanje ?? pokretanja.FirstOrDefault()?.PokretanjeId;
            if (izabrano is null)
                return Results.BadRequest(new { greska = "Nema nijednog pokretanja testa." });

            var redovi = await repo.RezultatiAsync(izabrano, ct);
            if (redovi.Count == 0)
                return Results.BadRequest(new { greska = "Pokretanje nema nijedan rezultat." });

            var info = pokretanja.First(x => x.PokretanjeId == izabrano);
            var nazivi = fabrika.DostupniModeli.ToDictionary(m => m.Id, m => m.PrikaznoIme);

            double PoTezini(string model, string tezina)
            {
                var deo = redovi.Where(r => r.ModelId == model && r.Tezina == tezina).ToList();
                return deo.Count == 0 ? 0 : MetricsCalculator.ZaGrupu(tezina, deo).ExecutionAccuracy;
            }

            var poModelu = MetricsCalculator.PoModelu(redovi)
                .OrderByDescending(x => x.Value.ExecutionAccuracy)
                .Select(x => new RedMetrike(
                    nazivi.GetValueOrDefault(x.Key, x.Key),
                    x.Value.Broj, x.Value.ExecutionAccuracy, x.Value.ValidSqlRate,
                    x.Value.ProsecnaOcenaSudije, x.Value.ProsecnoTrajanjeMs, x.Value.ProsecnoTokena,
                    PoTezini(x.Key, "lak"), PoTezini(x.Key, "srednji"), PoTezini(x.Key, "tezak")))
                .ToList();

            var pobednikId = await najbolji.OdrediAsync(ct);
            var pdf = PdfMetrike.Napravi(new PodaciMetrika(
                info.PokretanjeId, info.Pocetak, info.ModelSudije, redovi.Count,
                nazivi.GetValueOrDefault(pobednikId, pobednikId),
                poModelu,
                MetricsCalculator.IzracunajSlaganje(redovi)));

            return Results.File(pdf, "application/pdf",
                $"rezultati-testiranja-{info.Pocetak:yyyyMMdd}.pdf");
        });

        app.MapGet("/api/benchmark/izvoz.csv", async (
            int? pokretanje, BenchmarkRepository repo, CancellationToken ct) =>
        {
            var redovi = await repo.SviRedoviAsync(pokretanje, ct);
            var sb = new StringBuilder();
            sb.AppendLine("pokretanje_id,zadatak_id,tezina,model_id,jezik,sql_ispravan," +
                          "rezultat_isti,ocena_sudije,sudija_tacan,trajanje_ms," +
                          "ulazni_tokeni,izlazni_tokeni,generisani_sql,greska_izvrsavanja");

            foreach (IDictionary<string, object?> r in redovi)
            {
                sb.AppendLine(string.Join(',', new[]
                {
                    "pokretanje_id", "zadatak_id", "tezina", "model_id", "jezik",
                    "sql_ispravan", "rezultat_isti", "ocena_sudije", "sudija_tacan",
                    "trajanje_ms", "ulazni_tokeni", "izlazni_tokeni",
                    "generisani_sql", "greska_izvrsavanja"
                }.Select(k => CsvPolje(r.TryGetValue(k, out var v) ? v : null))));
            }

            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()),
                "text/csv; charset=utf-8", "benchmark-rezultati.csv");
        });
    }

    private static object SlaganjeDto(SlaganjeSudije s) => new
    {
        procenat = s.Slaganje,
        kappa = s.Kappa,
        opisKappe = MetricsCalculator.OpisKappe(s.Kappa),
        tacnoPozitivno = s.TacnoPozitivno,
        tacnoNegativno = s.TacnoNegativno,
        laznoPozitivno = s.LaznoPozitivno,
        laznoNegativno = s.LaznoNegativno
    };

    private static string CsvPolje(object? v)
    {
        var s = v switch
        {
            null => "",
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? ""
        };

        return s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? '"' + s.Replace("\"", "\"\"") + '"'
            : s;
    }
}
