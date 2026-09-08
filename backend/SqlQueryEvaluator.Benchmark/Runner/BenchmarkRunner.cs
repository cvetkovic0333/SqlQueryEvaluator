using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Evaluation;
using SqlQueryEvaluator.Core.Llm;
using SqlQueryEvaluator.Core.Persistence;
using SqlQueryEvaluator.Core.TextToSql;

namespace SqlQueryEvaluator.Benchmark.Runner;

/// <summary>
/// Offline testiranje svih modela nad test setom.
///
/// Za svaki par (zadatak, model, jezik):
///   1. model prevede pitanje u SQL,
///   2. sanitizer proveri upit,
///   3. izvrši se i generisani i gold SQL pa se porede REZULTATI
///      (execution accuracy — objektivna mera),
///   4. sudija nezavisno oceni upit ocenom 1-5.
///
/// Poslednja dva koraka se rade oba namerno: tek poređenjem ocene sudije sa
/// objektivnom merom vidi se koliko je LLM-as-a-Judge uopšte pouzdan.
/// </summary>
public sealed class BenchmarkRunner(
    ILlmProviderFactory fabrika,
    SchemaIntrospector introspektor,
    ExecutionAccuracyEvaluator poredjenje,
    LlmJudge sudija,
    BenchmarkRepository repo,
    IOptions<TextToSqlOptions> opcije)
{
    private readonly TextToSqlOptions _opcije = opcije.Value;

    public async Task<int> PokreniAsync(Argumenti arg, CancellationToken ct = default)
    {
        if (arg.Pomoc)
        {
            Console.WriteLine(Argumenti.TekstPomoci);
            return 0;
        }

        if (arg.SamoProvera)
            return await ProveriKljuceveAsync(ct);

        var testSet = await TestSet.UcitajAsync(arg.TestSetPutanja, ct);
        var zadaci = Filtriraj(testSet.Zadaci, arg);

        if (zadaci.Count == 0)
        {
            Console.WriteLine("Nijedan zadatak ne odgovara zadatim filterima.");
            return 1;
        }

        var modeli = OdrediModele(arg);
        if (modeli.Count == 0)
        {
            Console.WriteLine("Nijedan model nema podešen API ključ. Pokreni --dry-run za detalje.");
            return 1;
        }

        var modelSudije = arg.ModelSudije ?? _opcije.JudgeModelId;
        if (string.IsNullOrWhiteSpace(modelSudije) || !fabrika.ImaKljuc(modelSudije))
        {
            Console.WriteLine($"Sudija '{modelSudije}' nema API ključ — ocenjivanje se preskače.");
            modelSudije = "";
        }

        var ukupno = zadaci.Count * modeli.Count * arg.Jezici.Count;
        Console.WriteLine($"Zadataka: {zadaci.Count} · modela: {modeli.Count} · jezika: {arg.Jezici.Count}");
        Console.WriteLine($"Ukupno poziva: {ukupno}  (sudija: {(modelSudije == "" ? "nema" : modelSudije)})");
        Console.WriteLine();

        int pokretanjeId;
        HashSet<string> vecUradjeni;

        if (arg.NastaviPokretanje is { } postojece)
        {
            pokretanjeId = postojece;
            vecUradjeni = await repo.VecUradjeniAsync(pokretanjeId, ct);
            Console.WriteLine($"Nastavljam pokretanje #{pokretanjeId}; već urađeno: {vecUradjeni.Count}");
        }
        else
        {
            pokretanjeId = await repo.ZapocniPokretanjeAsync(modelSudije, zadaci.Count,
                $"modeli: {string.Join(", ", modeli)}", ct);
            vecUradjeni = [];
            Console.WriteLine($"Pokretanje #{pokretanjeId}");
        }

        Console.WriteLine();

        var sat = Stopwatch.StartNew();
        var uradjeno = 0;
        var preskoceno = 0;

        foreach (var model in modeli)
        {
            foreach (var zadatak in zadaci)
            {
                foreach (var jezik in arg.Jezici)
                {
                    ct.ThrowIfCancellationRequested();

                    var kljuc = $"{zadatak.Id}|{model}|{jezik}";
                    if (vecUradjeni.Contains(kljuc))
                    {
                        preskoceno++;
                        continue;
                    }

                    uradjeno++;
                    var oznaka = $"[{uradjeno + preskoceno}/{ukupno}] {model} · {zadatak.Id} · {jezik}";

                    // Kada provajder javi da je kvota probijena, čeka se tačno
                    // onoliko koliko on traži, pa se zadatak ponavlja. Slepo
                    // ponavljanje ovde samo troši dnevnu kvotu bez ijednog
                    // upotrebljivog rezultata.
                    var pokusaj = 0;
                    while (true)
                    {
                        try
                        {
                            var rezultat = await ObradiAsync(pokretanjeId, model, zadatak, jezik, modelSudije, ct);
                            await repo.UpisiRezultatAsync(rezultat, ct);

                            var znak = rezultat.RezultatIsti ? "✓" : rezultat.SqlIspravan ? "≈" : "✗";
                            Console.WriteLine($"{oznaka}  {znak}  sudija={rezultat.OcenaSudije?.ToString() ?? "-"}  {rezultat.TrajanjeMs}ms");
                            break;
                        }
                        catch (LlmException ex) when (ex.RateLimit && pokusaj < 3)
                        {
                            pokusaj++;
                            var cekaj = Math.Clamp(ex.CekajSekundi ?? 60, 5, 420);
                            Console.WriteLine($"{oznaka}  kvota probijena — čekam {cekaj}s (pokušaj {pokusaj}/3)");
                            await Task.Delay(TimeSpan.FromSeconds(cekaj), ct);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"{oznaka}  GREŠKA: {ex.Message}");
                            break;
                        }
                    }

                    // Besplatni tierovi imaju stroge rate limite; pauza između
                    // poziva je jeftinija od stalnog udaranja u 429.
                    if (arg.PauzaMs > 0)
                        await Task.Delay(arg.PauzaMs, ct);
                }
            }
        }

        await repo.ZavrsiPokretanjeAsync(pokretanjeId, ct);
        sat.Stop();

        Console.WriteLine();
        Console.WriteLine($"Gotovo za {sat.Elapsed:hh\\:mm\\:ss}. Novih: {uradjeno}, preskočeno: {preskoceno}.");

        await IzvestajAsync(pokretanjeId, ct);
        return 0;
    }

    private async Task<BenchmarkRezultatUpis> ObradiAsync(
        int pokretanjeId, string modelId, TestZadatak zadatak, string jezik,
        string modelSudije, CancellationToken ct)
    {
        var sema = await introspektor.UcitajAsync(zadatak.Baza, ct);
        var pitanje = zadatak.Pitanje(jezik);

        var provajder = fabrika.Kreiraj(modelId);
        var odgovor = await provajder.CompleteAsync(new LlmRequest(
            PromptBuilder.SistemskiPrompt,
            PromptBuilder.KorisnickiPrompt(sema, pitanje),
            Temperature: 0, MaxTokens: provajder.Opis.MaxTokens), ct);

        var provera = SqlSanitizer.Proveri(odgovor.Text);

        var sqlIspravan = false;
        var rezultatIsti = false;
        string? greska = provera.Prihvacen ? null : provera.Razlog;

        if (provera.Prihvacen)
        {
            var ishod = await poredjenje.UporediAsync(zadatak.GoldSql, provera.Sql, ct);
            rezultatIsti = ishod.Poklapa;
            sqlIspravan = !ishod.Objasnjenje.StartsWith("Generisani SQL se nije izvršio", StringComparison.Ordinal);
            if (!ishod.Poklapa) greska = ishod.Objasnjenje;
        }

        OcenaSudije? ocena = null;
        if (!string.IsNullOrEmpty(modelSudije))
        {
            ocena = await sudija.OceniAsync(
                modelSudije, sema, pitanje,
                provera.Prihvacen ? provera.Sql : odgovor.Text,
                zadatak.GoldSql, greska, ct);
        }

        return new BenchmarkRezultatUpis
        {
            PokretanjeId = pokretanjeId,
            ZadatakId = zadatak.Id,
            Tezina = zadatak.Tezina,
            ModelId = modelId,
            Jezik = jezik,
            GenerisaniSql = provera.Prihvacen ? provera.Sql : odgovor.Text,
            SqlIspravan = sqlIspravan,
            GreskaIzvrsavanja = greska,
            RezultatIsti = rezultatIsti,
            OcenaSudije = ocena?.Ocena is > 0 ? ocena.Ocena : null,
            SudijaTacan = ocena?.Ocena is > 0 ? ocena.Tacan : null,
            Obrazlozenje = ocena?.Obrazlozenje,
            TrajanjeMs = (int)odgovor.TrajanjeMs,
            UlazniTokeni = odgovor.UlazniTokeni,
            IzlazniTokeni = odgovor.IzlazniTokeni
        };
    }

    /// <summary>Provera da li svaki konfigurisan model ima ključ i odgovara na trivijalan poziv.</summary>
    private async Task<int> ProveriKljuceveAsync(CancellationToken ct)
    {
        Console.WriteLine("Provera modela iz registra (appsettings.json → Models):");
        Console.WriteLine();

        var ispravnih = 0;

        foreach (var opis in fabrika.DostupniModeli)
        {
            Console.Write($"  {opis.Id,-28} ");

            if (!fabrika.ImaKljuc(opis.Id))
            {
                Console.WriteLine($"nema ključ (ApiKeys:{opis.ApiKeyRef})");
                continue;
            }

            try
            {
                var provajder = fabrika.Kreiraj(opis.Id);

                // MaxTokens mora da bude izdašan i za ovako trivijalan poziv:
                // modeli koji "razmišljaju" pre odgovora (GPT-OSS, Qwen3)
                // potroše mali budžet na razmišljanje i vrate prazan tekst,
                // pa bi ispravan model izgledao kao pokvaren.
                var odgovor = await provajder.CompleteAsync(new LlmRequest(
                    "You are a test probe. Reply with exactly: OK",
                    "Reply with exactly: OK", MaxTokens: 512), ct);

                if (string.IsNullOrWhiteSpace(odgovor.Text))
                {
                    Console.WriteLine($"odgovorio, ali PRAZAN tekst ({odgovor.TrajanjeMs} ms, " +
                                      $"{odgovor.IzlazniTokeni} izlaznih tokena) — proveri MaxTokens");
                }
                else
                {
                    Console.WriteLine($"radi  ({odgovor.TrajanjeMs} ms, odgovor: \"{Skrati(odgovor.Text)}\")");
                }

                ispravnih++;
            }
            catch (LlmException ex)
            {
                Console.WriteLine($"GREŠKA{(ex.RateLimit ? " (rate limit)" : "")}: {Skrati(ex.Message, 90)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Ispravnih modela: {ispravnih} od {fabrika.DostupniModeli.Count}");
        return ispravnih > 0 ? 0 : 1;
    }

    private async Task IzvestajAsync(int pokretanjeId, CancellationToken ct)
    {
        var redovi = await repo.RezultatiAsync(pokretanjeId, ct);
        if (redovi.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine($"{"Model",-28} {"Tačnost",8} {"ValidSQL",9} {"Sudija",7} {"ms",7}");
        Console.WriteLine(new string('-', 64));

        foreach (var (model, m) in MetricsCalculator.PoModelu(redovi)
                     .OrderByDescending(p => p.Value.ExecutionAccuracy))
        {
            Console.WriteLine($"{model,-28} {m.ExecutionAccuracy,7:0.0}% {m.ValidSqlRate,8:0.0}% " +
                              $"{m.ProsecnaOcenaSudije,7:0.00} {m.ProsecnoTrajanjeMs,7:0}");
        }

        var slaganje = MetricsCalculator.IzracunajSlaganje(redovi);
        Console.WriteLine();
        Console.WriteLine($"Slaganje sudije sa stvarnim rezultatom: {slaganje.Slaganje:0.0}%  " +
                          $"(κ = {slaganje.Kappa:0.000} — {MetricsCalculator.OpisKappe(slaganje.Kappa)})");

        await SacuvajIzlazAsync(pokretanjeId, redovi, ct);
    }

    /// <summary>
    /// Rezultati se čuvaju i u fajl, pored baze — u radu idu kao prilog, a
    /// zapis nosi i tačan datum pokretanja, jer se ponuda besplatnih modela
    /// vremenom menja.
    /// </summary>
    private static async Task SacuvajIzlazAsync(
        int pokretanjeId, IReadOnlyList<RezultatZaMetriku> redovi, CancellationToken ct)
    {
        var folder = Path.Combine("benchmark", "results");
        Directory.CreateDirectory(folder);

        var json = Path.Combine(folder, $"pokretanje-{pokretanjeId}.json");
        await File.WriteAllTextAsync(json, JsonSerializer.Serialize(new
        {
            pokretanjeId,
            datum = DateTimeOffset.Now,
            poModelu = MetricsCalculator.PoModelu(redovi),
            poTezini = MetricsCalculator.PoTezini(redovi),
            poJeziku = MetricsCalculator.PoJeziku(redovi),
            slaganjeSudije = MetricsCalculator.IzracunajSlaganje(redovi)
        }, new JsonSerializerOptions { WriteIndented = true }), ct);

        var csv = Path.Combine(folder, $"pokretanje-{pokretanjeId}.csv");
        var sb = new StringBuilder("model_id,tezina,jezik,sql_ispravan,rezultat_isti,ocena_sudije,sudija_tacan,trajanje_ms\n");
        foreach (var r in redovi)
        {
            sb.Append(string.Join(',',
                r.ModelId, r.Tezina, r.Jezik, r.SqlIspravan, r.RezultatIsti,
                r.OcenaSudije?.ToString(CultureInfo.InvariantCulture) ?? "",
                r.SudijaTacan?.ToString() ?? "", r.TrajanjeMs)).Append('\n');
        }
        await File.WriteAllTextAsync(csv, sb.ToString(), ct);

        Console.WriteLine($"Rezultati sačuvani: {json} i {csv}");
    }

    private List<string> OdrediModele(Argumenti arg)
    {
        var svi = fabrika.DostupniModeli.Select(m => m.Id).ToList();
        var trazeni = arg.Modeli.Count > 0 ? arg.Modeli : svi;

        var saKljucem = new List<string>();
        foreach (var m in trazeni)
        {
            if (!svi.Contains(m))
                Console.WriteLine($"Upozorenje: model '{m}' nije u registru — preskačem.");
            else if (!fabrika.ImaKljuc(m))
                Console.WriteLine($"Upozorenje: model '{m}' nema API ključ — preskačem.");
            else
                saKljucem.Add(m);
        }

        return saKljucem;
    }

    private static List<TestZadatak> Filtriraj(List<TestZadatak> zadaci, Argumenti arg)
    {
        IEnumerable<TestZadatak> upit = zadaci;

        if (arg.Tezine.Count > 0)
            upit = upit.Where(z => arg.Tezine.Contains(z.Tezina, StringComparer.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(arg.Baza))
            upit = upit.Where(z => z.Baza.Equals(arg.Baza, StringComparison.OrdinalIgnoreCase));

        if (arg.Limit is { } n)
            upit = upit.Take(n);

        return upit.ToList();
    }

    private static string Skrati(string s, int n = 40) =>
        s.Length <= n ? s.Replace("\n", " ") : s[..n].Replace("\n", " ") + "…";
}
