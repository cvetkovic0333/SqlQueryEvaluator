using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using SqlQueryEvaluator.Core.Models;
using SqlQueryEvaluator.Core.Persistence;

namespace SqlQueryEvaluator.Core.TextToSql;

/// <summary>
/// Čita strukturu šeme iz information_schema i pg_description.
/// Rezultat se kešira jer se šema ne menja u toku rada aplikacije.
///
/// Koristi vezu aplikacije, a ne rolu za čitanje — komentari na tabelama i
/// kolonama su deo prompta koji ide modelu, a rola sqleval_citanje nema
/// pristup svim sistemskim katalozima.
/// </summary>
public sealed class SchemaIntrospector(AplikacijaDataSource izvor, IMemoryCache kes)
{
    private static readonly TimeSpan TrajanjeKesa = TimeSpan.FromMinutes(30);

    public async Task<DatabaseSchema> UcitajAsync(string sema, CancellationToken ct = default)
    {
        if (kes.TryGetValue<DatabaseSchema>($"sema:{sema}", out var iz) && iz is not null)
            return iz;

        var ucitana = await UcitajBezKesaAsync(sema, ct);
        kes.Set($"sema:{sema}", ucitana, TrajanjeKesa);
        return ucitana;
    }

    public void OcistiKes(string sema) => kes.Remove($"sema:{sema}");

    private async Task<DatabaseSchema> UcitajBezKesaAsync(string sema, CancellationToken ct)
    {
        await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);

        var opisSeme = await veza.ExecuteScalarAsync<string?>(
            "select obj_description(n.oid, 'pg_namespace') from pg_namespace n where n.nspname = @sema",
            new { sema });

        var kolone = (await veza.QueryAsync<KolonaRed>(
            """
            SELECT c.table_name                                   AS Tabela,
                   c.column_name                                  AS Kolona,
                   c.data_type                                    AS Tip,
                   (c.is_nullable = 'YES')                        AS MozeBitiNull,
                   c.ordinal_position                             AS Redosled,
                   col_description(k.oid, c.ordinal_position)     AS OpisKolone,
                   obj_description(k.oid, 'pg_class')             AS OpisTabele
            FROM information_schema.columns c
            JOIN pg_class k     ON k.relname = c.table_name
            JOIN pg_namespace n ON n.oid = k.relnamespace AND n.nspname = c.table_schema
            WHERE c.table_schema = @sema AND k.relkind = 'r'
            ORDER BY c.table_name, c.ordinal_position
            """, new { sema })).ToList();

        var ogranicenja = (await veza.QueryAsync<OgranicenjeRed>(
            """
            SELECT tc.table_name        AS Tabela,
                   kcu.column_name      AS Kolona,
                   tc.constraint_type   AS Tip,
                   ccu.table_name       AS CiljnaTabela,
                   ccu.column_name      AS CiljnaKolona
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                 ON kcu.constraint_name = tc.constraint_name
                AND kcu.constraint_schema = tc.constraint_schema
            LEFT JOIN information_schema.constraint_column_usage ccu
                 ON ccu.constraint_name = tc.constraint_name
                AND ccu.constraint_schema = tc.constraint_schema
            WHERE tc.table_schema = @sema
              AND tc.constraint_type IN ('PRIMARY KEY','FOREIGN KEY')
            """, new { sema })).ToList();

        var primarni = ogranicenja
            .Where(o => o.Tip == "PRIMARY KEY")
            .Select(o => (o.Tabela, o.Kolona))
            .ToHashSet();

        var tabele = new List<TableInfo>();
        foreach (var grupa in kolone.GroupBy(k => k.Tabela).OrderBy(g => g.Key))
        {
            var tabela = new TableInfo
            {
                Sema = sema,
                Naziv = grupa.Key,
                Opis = grupa.First().OpisTabele
            };

            foreach (var k in grupa.OrderBy(k => k.Redosled))
            {
                tabela.Kolone.Add(new ColumnInfo
                {
                    Naziv = k.Kolona,
                    Tip = SkratiTip(k.Tip),
                    MozeBitiNull = k.MozeBitiNull,
                    PrimarniKljuc = primarni.Contains((k.Tabela, k.Kolona)),
                    Opis = k.OpisKolone
                });
            }

            foreach (var fk in ogranicenja.Where(o => o.Tip == "FOREIGN KEY" && o.Tabela == grupa.Key))
            {
                if (fk.CiljnaTabela is null || fk.CiljnaKolona is null) continue;
                tabela.StraniKljucevi.Add(new ForeignKeyInfo
                {
                    Kolona = fk.Kolona,
                    CiljnaTabela = fk.CiljnaTabela,
                    CiljnaKolona = fk.CiljnaKolona
                });
            }

            tabele.Add(tabela);
        }

        await UcitajBrojRedovaAsync(veza, sema, tabele);

        return new DatabaseSchema { Naziv = sema, Opis = opisSeme, Tabele = tabele };
    }

    /// <summary>
    /// Tačan broj redova po tabeli, u jednom upitu. Na ovim veličinama
    /// (do 5000 redova) COUNT je trenutan, a procena iz pg_class ume da
    /// promaši, pa se u interfejsu ne bi poklapala sa prikazanim podacima.
    /// </summary>
    private static async Task UcitajBrojRedovaAsync(
        NpgsqlConnection veza, string sema, List<TableInfo> tabele)
    {
        if (tabele.Count == 0) return;

        var delovi = tabele.Select(t =>
            $"SELECT '{t.Naziv}' AS tabela, count(*) AS broj FROM \"{sema}\".\"{t.Naziv}\"");
        var upit = string.Join(" UNION ALL ", delovi);

        var brojevi = (await veza.QueryAsync<BrojRed>(upit))
            .ToDictionary(r => r.Tabela, r => r.Broj);

        foreach (var t in tabele)
            t.BrojRedova = brojevi.GetValueOrDefault(t.Naziv);
    }

    /// <summary>information_schema vraća duge nazive tipova; kraći su čitljiviji i modelu i korisniku.</summary>
    private static string SkratiTip(string tip) => tip switch
    {
        "character varying" => "varchar",
        "timestamp with time zone" => "timestamptz",
        "timestamp without time zone" => "timestamp",
        "double precision" => "float8",
        "ARRAY" => "array",
        _ => tip
    };

    private sealed record KolonaRed(
        string Tabela, string Kolona, string Tip, bool MozeBitiNull,
        int Redosled, string? OpisKolone, string? OpisTabele);

    private sealed record OgranicenjeRed(
        string Tabela, string Kolona, string Tip, string? CiljnaTabela, string? CiljnaKolona);

    private sealed record BrojRed(string Tabela, long Broj);
}
