using SqlQueryEvaluator.Core.TextToSql;

namespace SqlQueryEvaluator.Tests;

/// <summary>
/// Sanitizer je drugi sloj zaštite pri izvršavanju SQL-a koji je generisao
/// model, pa je i najvažniji za testiranje.
/// </summary>
public class SqlSanitizerTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("select ime from prodavnica.kupci where grad = 'Niš'")]
    [InlineData("WITH x AS (SELECT 1 AS a) SELECT a FROM x")]
    [InlineData("SELECT count(*) FROM prodavnica.porudzbine WHERE status = 'otkazana'")]
    public void Prihvata_ispravne_select_upite(string sql)
    {
        var r = SqlSanitizer.Proveri(sql);
        Assert.True(r.Prihvacen, r.Razlog);
    }

    [Theory]
    [InlineData("DELETE FROM prodavnica.kupci")]
    [InlineData("UPDATE prodavnica.kupci SET ime = 'x'")]
    [InlineData("INSERT INTO prodavnica.kupci (ime) VALUES ('x')")]
    [InlineData("DROP TABLE prodavnica.kupci")]
    [InlineData("TRUNCATE prodavnica.kupci")]
    [InlineData("GRANT ALL ON prodavnica.kupci TO public")]
    public void Odbija_izmene_podataka(string sql)
    {
        Assert.False(SqlSanitizer.Proveri(sql).Prihvacen);
    }

    [Fact]
    public void Odbija_vise_statement_a()
    {
        var r = SqlSanitizer.Proveri("SELECT 1; DROP TABLE prodavnica.kupci");
        Assert.False(r.Prihvacen);
        Assert.Contains("jedan", r.Razlog!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dozvoljava_zavrsni_tacka_zarez()
    {
        var r = SqlSanitizer.Proveri("SELECT 1;");
        Assert.True(r.Prihvacen);
        Assert.DoesNotContain(";", r.Sql);
    }

    [Fact]
    public void Nalazi_opasnu_rec_sakrivenu_iza_linijskog_komentara()
    {
        // Model ume da vrati upit sa komentarom u kome je druga komanda.
        // Posle uklanjanja komentara mora da ostane samo bezopasan deo.
        var r = SqlSanitizer.Proveri("SELECT 1\n--\nDROP TABLE prodavnica.kupci");
        Assert.False(r.Prihvacen);
    }

    [Fact]
    public void Uklanja_blok_komentar_ali_ne_i_ono_iza_njega()
    {
        var r = SqlSanitizer.Proveri("SELECT /* komentar */ 1");
        Assert.True(r.Prihvacen);
        Assert.DoesNotContain("komentar", r.Sql);
    }

    [Fact]
    public void Odbija_data_modifying_cte()
    {
        var sql = "WITH x AS (UPDATE prodavnica.kupci SET ime='x' RETURNING 1) SELECT * FROM x";
        Assert.False(SqlSanitizer.Proveri(sql).Prihvacen);
    }

    [Fact]
    public void Odbija_dollar_quoting()
    {
        Assert.False(SqlSanitizer.Proveri("SELECT $$tekst$$").Prihvacen);
        Assert.False(SqlSanitizer.Proveri("SELECT $tag$tekst$tag$").Prihvacen);
    }

    [Fact]
    public void Odbija_sistemske_kataloge_i_opasne_funkcije()
    {
        Assert.False(SqlSanitizer.Proveri("SELECT * FROM pg_catalog.pg_authid").Prihvacen);
        Assert.False(SqlSanitizer.Proveri("SELECT * FROM information_schema.tables").Prihvacen);
        Assert.False(SqlSanitizer.Proveri("SELECT pg_sleep(10)").Prihvacen);
        Assert.False(SqlSanitizer.Proveri("SELECT pg_read_file('/etc/passwd')").Prihvacen);
    }

    [Fact]
    public void Ne_zabunjuje_ga_kljucna_rec_unutar_string_literala()
    {
        // Ovo je legitiman upit: reč "otkazana" je PODATAK, ne komanda.
        // Bez maskiranja string literala provera bi ga pogrešno odbila.
        var r = SqlSanitizer.Proveri(
            "SELECT count(*) FROM prodavnica.porudzbine WHERE status = 'otkazana'");
        Assert.True(r.Prihvacen, r.Razlog);
    }

    [Fact]
    public void Ne_zabunjuje_ga_rec_delete_unutar_teksta()
    {
        var r = SqlSanitizer.Proveri("SELECT * FROM prodavnica.recenzije WHERE komentar ILIKE '%delete%'");
        Assert.True(r.Prihvacen, r.Razlog);
    }

    [Fact]
    public void Ne_zabunjuje_ga_offset()
    {
        // "offset" sadrži "set", ali granica reči sprečava lažno podudaranje.
        var r = SqlSanitizer.Proveri("SELECT ime FROM prodavnica.kupci LIMIT 10 OFFSET 20");
        Assert.True(r.Prihvacen, r.Razlog);
    }

    [Fact]
    public void Skida_markdown_ogradu_koju_modeli_dodaju()
    {
        var r = SqlSanitizer.Proveri("```sql\nSELECT 1\n```");
        Assert.True(r.Prihvacen, r.Razlog);
        Assert.DoesNotContain("`", r.Sql);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- samo komentar")]
    public void Odbija_prazan_odgovor(string? sql)
    {
        Assert.False(SqlSanitizer.Proveri(sql).Prihvacen);
    }

    [Fact]
    public void Uklanja_think_blok_koji_modeli_koji_razmisljaju_dodaju()
    {
        // Qwen3 i slični ispisuju tok razmišljanja pre odgovora. Bez
        // uklanjanja bloka upit počinje tekstom i biva odbijen, iako je
        // model ispod dao potpuno ispravan SQL.
        var odgovor = """
            <think>
            Provericu prvo tabelu kupci, pa cu spojiti sa porudzbinama...
            </think>
            SELECT ime FROM prodavnica.kupci
            """;

        var r = SqlSanitizer.Proveri(odgovor);
        Assert.True(r.Prihvacen, r.Razlog);
        Assert.StartsWith("SELECT", r.Sql);
        Assert.DoesNotContain("think", r.Sql);
    }

    [Fact]
    public void Odbija_odgovor_koji_je_samo_nedovrseno_razmisljanje()
    {
        // Model je dostigao granicu tokena usred razmišljanja — nema SQL-a.
        var r = SqlSanitizer.Proveri("<think>Hajde da razmislim o ovom upitu");
        Assert.False(r.Prihvacen);
        Assert.Contains("razmišljanja", r.Razlog!);
    }
}
