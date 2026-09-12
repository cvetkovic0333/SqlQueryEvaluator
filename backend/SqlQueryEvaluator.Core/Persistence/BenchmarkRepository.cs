using Dapper;
using SqlQueryEvaluator.Core.Evaluation;

namespace SqlQueryEvaluator.Core.Persistence;

public sealed class BenchmarkRezultatUpis
{
    public int PokretanjeId { get; init; }
    public string ZadatakId { get; init; } = "";
    public string Tezina { get; init; } = "lak";
    public string ModelId { get; init; } = "";
    public string Jezik { get; init; } = "sr";
    public string? GenerisaniSql { get; init; }
    public bool SqlIspravan { get; init; }
    public string? GreskaIzvrsavanja { get; init; }
    public bool RezultatIsti { get; init; }
    public int? OcenaSudije { get; init; }
    public bool? SudijaTacan { get; init; }
    public string? Obrazlozenje { get; init; }
    public int TrajanjeMs { get; init; }
    public int UlazniTokeni { get; init; }
    public int IzlazniTokeni { get; init; }
}

/// <summary>Sačuvan odgovor modela, za ponovno poređenje bez novog poziva.</summary>
public sealed class SacuvanUpit
{
    public long RezultatId { get; init; }
    public string ZadatakId { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string? GenerisaniSql { get; init; }
    public bool SqlIspravan { get; init; }
    public bool RezultatIsti { get; init; }
    public string? GreskaIzvrsavanja { get; init; }
}

public sealed class PokretanjeInfo
{
    public int PokretanjeId { get; init; }
    public DateTimeOffset Pocetak { get; init; }
    public DateTimeOffset? Kraj { get; init; }
    public string ModelSudije { get; init; } = "";
    public int? BrojZadataka { get; init; }
    public string? Napomena { get; init; }
}

public sealed class BenchmarkRepository(AplikacijaDataSource izvor)
{
    public async Task<int> ZapocniPokretanjeAsync(
        string modelSudije, int brojZadataka, string? napomena, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        return await veza.ExecuteScalarAsync<int>(
            """
            INSERT INTO aplikacija.benchmark_pokretanje (model_sudije, broj_zadataka, napomena)
            VALUES (@modelSudije, @brojZadataka, @napomena)
            RETURNING pokretanje_id
            """, new { modelSudije, brojZadataka, napomena });
    }

    public async Task ZavrsiPokretanjeAsync(int pokretanjeId, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        await veza.ExecuteAsync(
            "UPDATE aplikacija.benchmark_pokretanje SET kraj = now() WHERE pokretanje_id = @pokretanjeId",
            new { pokretanjeId });
    }

    /// <summary>
    /// Upis je idempotentan po (pokretanje, zadatak, model, jezik) — zato
    /// --resume može da nastavi prekinuto pokretanje bez ponovnog trošenja
    /// kvote besplatnog API tiera.
    /// </summary>
    public async Task UpisiRezultatAsync(BenchmarkRezultatUpis r, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        await veza.ExecuteAsync(
            """
            INSERT INTO aplikacija.benchmark_rezultat
                (pokretanje_id, zadatak_id, tezina, model_id, jezik, generisani_sql,
                 sql_ispravan, greska_izvrsavanja, rezultat_isti, ocena_sudije,
                 sudija_tacan, obrazlozenje, trajanje_ms, ulazni_tokeni, izlazni_tokeni)
            VALUES
                (@PokretanjeId, @ZadatakId, @Tezina, @ModelId, @Jezik, @GenerisaniSql,
                 @SqlIspravan, @GreskaIzvrsavanja, @RezultatIsti, @OcenaSudije,
                 @SudijaTacan, @Obrazlozenje, @TrajanjeMs, @UlazniTokeni, @IzlazniTokeni)
            ON CONFLICT (pokretanje_id, zadatak_id, model_id, jezik) DO UPDATE SET
                generisani_sql = EXCLUDED.generisani_sql,
                sql_ispravan = EXCLUDED.sql_ispravan,
                greska_izvrsavanja = EXCLUDED.greska_izvrsavanja,
                rezultat_isti = EXCLUDED.rezultat_isti,
                ocena_sudije = EXCLUDED.ocena_sudije,
                sudija_tacan = EXCLUDED.sudija_tacan,
                obrazlozenje = EXCLUDED.obrazlozenje,
                trajanje_ms = EXCLUDED.trajanje_ms,
                ulazni_tokeni = EXCLUDED.ulazni_tokeni,
                izlazni_tokeni = EXCLUDED.izlazni_tokeni
            """, r);
    }

    public async Task<IReadOnlyList<SacuvanUpit>> SacuvaniUpitiAsync(int pokretanjeId, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        var redovi = await veza.QueryAsync<SacuvanUpit>(
            """
            SELECT rezultat_id AS RezultatId, zadatak_id AS ZadatakId, model_id AS ModelId,
                   generisani_sql AS GenerisaniSql, sql_ispravan AS SqlIspravan,
                   rezultat_isti AS RezultatIsti, greska_izvrsavanja AS GreskaIzvrsavanja
              FROM aplikacija.benchmark_rezultat
             WHERE pokretanje_id = @pokretanjeId
             ORDER BY model_id, zadatak_id, jezik
            """, new { pokretanjeId });
        return redovi.ToList();
    }

    /// <summary>
    /// Menja samo ishod poređenja. Odgovor modela i ocena sudije ostaju
    /// netaknuti — oni ne zavise od pravila poređenja.
    /// </summary>
    public async Task AzurirajPoredjenjeAsync(
        long rezultatId, bool sqlIspravan, bool rezultatIsti, string? greska, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        await veza.ExecuteAsync(
            """
            UPDATE aplikacija.benchmark_rezultat
               SET sql_ispravan = @sqlIspravan,
                   rezultat_isti = @rezultatIsti,
                   greska_izvrsavanja = @greska
             WHERE rezultat_id = @rezultatId
            """, new { rezultatId, sqlIspravan, rezultatIsti, greska });
    }

    public async Task<HashSet<string>> VecUradjeniAsync(int pokretanjeId, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        var kljucevi = await veza.QueryAsync<string>(
            """
            SELECT zadatak_id || '|' || model_id || '|' || jezik
              FROM aplikacija.benchmark_rezultat
             WHERE pokretanje_id = @pokretanjeId
            """, new { pokretanjeId });
        return kljucevi.ToHashSet();
    }

    public async Task<PokretanjeInfo?> PoslednjePokretanjeAsync(CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        return await veza.QueryFirstOrDefaultAsync<PokretanjeInfo>(
            """
            SELECT pokretanje_id AS PokretanjeId, pocetak AS Pocetak, kraj AS Kraj,
                   model_sudije AS ModelSudije, broj_zadataka AS BrojZadataka,
                   napomena AS Napomena
              FROM aplikacija.benchmark_pokretanje
             ORDER BY pocetak DESC
             LIMIT 1
            """);
    }

    public async Task<IReadOnlyList<PokretanjeInfo>> SvaPokretanjaAsync(CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        var redovi = await veza.QueryAsync<PokretanjeInfo>(
            """
            SELECT pokretanje_id AS PokretanjeId, pocetak AS Pocetak, kraj AS Kraj,
                   model_sudije AS ModelSudije, broj_zadataka AS BrojZadataka,
                   napomena AS Napomena
              FROM aplikacija.benchmark_pokretanje
             ORDER BY pocetak DESC
            """);
        return redovi.ToList();
    }

    public async Task<IReadOnlyList<RezultatZaMetriku>> RezultatiAsync(
        int? pokretanjeId = null, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        var redovi = await veza.QueryAsync<RezultatZaMetriku>(
            """
            SELECT model_id AS ModelId, tezina AS Tezina, jezik AS Jezik,
                   sql_ispravan AS SqlIspravan, rezultat_isti AS RezultatIsti,
                   ocena_sudije AS OcenaSudije, sudija_tacan AS SudijaTacan,
                   coalesce(trajanje_ms, 0) AS TrajanjeMs,
                   coalesce(ulazni_tokeni, 0) AS UlazniTokeni,
                   coalesce(izlazni_tokeni, 0) AS IzlazniTokeni
              FROM aplikacija.benchmark_rezultat
             WHERE (@pokretanjeId IS NULL OR pokretanje_id = @pokretanjeId)
            """, new { pokretanjeId });
        return redovi.ToList();
    }

    /// <summary>Sirovi redovi za izvoz u CSV — u radu idu kao prilog.</summary>
    public async Task<IReadOnlyList<dynamic>> SviRedoviAsync(
        int? pokretanjeId = null, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        var redovi = await veza.QueryAsync(
            """
            SELECT pokretanje_id, zadatak_id, tezina, model_id, jezik,
                   sql_ispravan, rezultat_isti, ocena_sudije, sudija_tacan,
                   trajanje_ms, ulazni_tokeni, izlazni_tokeni,
                   replace(coalesce(generisani_sql, ''), E'\n', ' ') AS generisani_sql,
                   coalesce(greska_izvrsavanja, '') AS greska_izvrsavanja
              FROM aplikacija.benchmark_rezultat
             WHERE (@pokretanjeId IS NULL OR pokretanje_id = @pokretanjeId)
             ORDER BY model_id, zadatak_id, jezik
            """, new { pokretanjeId });
        return redovi.ToList();
    }
}
