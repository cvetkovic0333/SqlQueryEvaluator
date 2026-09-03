using Dapper;

namespace SqlQueryEvaluator.Core.Persistence;

public class IstorijaUpisa
{
    public string Pitanje { get; init; } = "";
    public string Jezik { get; init; } = "sr";
    public string Baza { get; init; } = "";
    public string[] IzabraneTabele { get; init; } = [];
    public string ModelId { get; init; } = "";
    public string? GenerisaniSql { get; init; }
    public int? OcenaSudije { get; init; }
    public string? Obrazlozenje { get; init; }
    public bool Izvrsen { get; init; }
    public int? BrojRedova { get; init; }
    public int? TrajanjeMs { get; init; }
    public string? Greska { get; init; }
}

public sealed class IstorijaStavka : IstorijaUpisa
{
    public long UpitId { get; init; }
    public DateTimeOffset Kreirano { get; init; }
}

public sealed class IstorijaRepository(AplikacijaDataSource izvor)
{
    public async Task<long> DodajAsync(IstorijaUpisa upis, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        return await veza.ExecuteScalarAsync<long>(
            """
            INSERT INTO aplikacija.istorija_upita
                (pitanje, jezik, baza, izabrane_tabele, model_id, generisani_sql,
                 ocena_sudije, obrazlozenje, izvrsen, broj_redova, trajanje_ms, greska)
            VALUES
                (@Pitanje, @Jezik, @Baza, @IzabraneTabele, @ModelId, @GenerisaniSql,
                 @OcenaSudije, @Obrazlozenje, @Izvrsen, @BrojRedova, @TrajanjeMs, @Greska)
            RETURNING upit_id
            """, upis);
    }

    public async Task OznaciIzvrsenAsync(
        long upitId, int brojRedova, int trajanjeMs, string? greska, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        await veza.ExecuteAsync(
            """
            UPDATE aplikacija.istorija_upita
               SET izvrsen = true, broj_redova = @brojRedova,
                   trajanje_ms = @trajanjeMs, greska = @greska
             WHERE upit_id = @upitId
            """, new { upitId, brojRedova, trajanjeMs, greska });
    }

    public async Task<IReadOnlyList<IstorijaStavka>> PoslednjiAsync(int koliko = 50, CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        var redovi = await veza.QueryAsync<IstorijaStavka>(
            """
            SELECT upit_id AS UpitId, pitanje AS Pitanje, jezik AS Jezik, baza AS Baza,
                   izabrane_tabele AS IzabraneTabele, model_id AS ModelId,
                   generisani_sql AS GenerisaniSql, ocena_sudije AS OcenaSudije,
                   obrazlozenje AS Obrazlozenje, izvrsen AS Izvrsen,
                   broj_redova AS BrojRedova, trajanje_ms AS TrajanjeMs,
                   greska AS Greska, kreirano AS Kreirano
              FROM aplikacija.istorija_upita
             ORDER BY kreirano DESC
             LIMIT @koliko
            """, new { koliko });
        return redovi.ToList();
    }

    public async Task ObrisiSveAsync(CancellationToken ct = default)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
        await veza.ExecuteAsync("TRUNCATE aplikacija.istorija_upita RESTART IDENTITY");
    }
}
