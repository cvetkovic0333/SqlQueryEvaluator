using SqlQueryEvaluator.Core.Evaluation;
using SqlQueryEvaluator.Core.Models;
using SqlQueryEvaluator.Core.TextToSql;

namespace SqlQueryEvaluator.Tests;

public class PromptBuilderTests
{
    private static DatabaseSchema Sema() => new()
    {
        Naziv = "prodavnica",
        Opis = "Online prodavnica",
        Tabele =
        [
            new TableInfo
            {
                Sema = "prodavnica", Naziv = "kupci", Opis = "Kupci",
                Kolone =
                [
                    new ColumnInfo { Naziv = "kupac_id", Tip = "integer", PrimarniKljuc = true },
                    new ColumnInfo { Naziv = "ime", Tip = "text", Opis = "Ime kupca" }
                ]
            },
            new TableInfo
            {
                Sema = "prodavnica", Naziv = "porudzbine", Opis = "Porudžbine",
                Kolone =
                [
                    new ColumnInfo { Naziv = "porudzbina_id", Tip = "integer", PrimarniKljuc = true },
                    new ColumnInfo { Naziv = "kupac_id", Tip = "integer" }
                ],
                StraniKljucevi =
                [
                    new ForeignKeyInfo { Kolona = "kupac_id", CiljnaTabela = "kupci", CiljnaKolona = "kupac_id" }
                ]
            },
            new TableInfo
            {
                Sema = "prodavnica", Naziv = "zaposleni", Opis = "Zaposleni",
                Kolone = [new ColumnInfo { Naziv = "zaposleni_id", Tip = "integer", PrimarniKljuc = true }]
            }
        ]
    };

    [Fact]
    public void Bez_izbora_tabela_salje_celu_semu()
    {
        var opis = PromptBuilder.OpisSeme(Sema());
        Assert.Contains("prodavnica.kupci", opis);
        Assert.Contains("prodavnica.porudzbine", opis);
        Assert.Contains("prodavnica.zaposleni", opis);
    }

    [Fact]
    public void Izbor_tabele_povlaci_i_tabelu_na_koju_pokazuje_strani_kljuc()
    {
        var opis = PromptBuilder.OpisSeme(Sema(), ["porudzbine"]);

        Assert.Contains("prodavnica.porudzbine", opis);
        Assert.Contains("prodavnica.kupci", opis);
        Assert.DoesNotContain("prodavnica.zaposleni", opis);
    }

    [Fact]
    public void Opis_sadrzi_komentare_kolona_i_strane_kljuceve()
    {
        var opis = PromptBuilder.OpisSeme(Sema());
        Assert.Contains("Ime kupca", opis);
        Assert.Contains("FK: prodavnica.porudzbine.kupac_id -> prodavnica.kupci.kupac_id", opis);
    }

    [Fact]
    public void Korisnicki_prompt_sadrzi_pitanje_i_semu()
    {
        var prompt = PromptBuilder.KorisnickiPrompt(Sema(), "Koliko ima kupaca?");
        Assert.Contains("Koliko ima kupaca?", prompt);
        Assert.Contains("CREATE TABLE prodavnica.kupci", prompt);
    }

    [Fact]
    public void Sistemski_prompt_zabranjuje_izmene_podataka()
    {
        Assert.Contains("SELECT or WITH", PromptBuilder.SistemskiPrompt);
        Assert.Contains("Never emit INSERT", PromptBuilder.SistemskiPrompt);
    }
}

public class TestSetTests
{
    private static string PutanjaTestSeta()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "SqlQueryEvaluator.sln")))
            d = d.Parent;

        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "benchmark", "testset.json");
    }

    [Fact]
    public async Task Test_set_se_ucitava_i_ima_45_zadataka()
    {
        var set = await TestSet.UcitajAsync(PutanjaTestSeta());
        Assert.Equal(45, set.Zadaci.Count);
    }

    [Fact]
    public async Task Ima_po_petnaest_zadataka_svake_tezine()
    {
        var set = await TestSet.UcitajAsync(PutanjaTestSeta());
        foreach (var tezina in new[] { "lak", "srednji", "tezak" })
            Assert.Equal(15, set.Zadaci.Count(z => z.Tezina == tezina));
    }

    [Fact]
    public async Task Svaki_zadatak_ima_pitanje_na_oba_jezika()
    {
        var set = await TestSet.UcitajAsync(PutanjaTestSeta());
        foreach (var z in set.Zadaci)
        {
            Assert.False(string.IsNullOrWhiteSpace(z.PitanjeSr), $"{z.Id}: nedostaje srpski");
            Assert.False(string.IsNullOrWhiteSpace(z.PitanjeEn), $"{z.Id}: nedostaje engleski");
        }
    }

    [Fact]
    public async Task Svi_gold_upiti_prolaze_sanitizer()
    {
        var set = await TestSet.UcitajAsync(PutanjaTestSeta());
        foreach (var z in set.Zadaci)
        {
            var r = SqlSanitizer.Proveri(z.GoldSql);
            Assert.True(r.Prihvacen, $"{z.Id}: {r.Razlog}");
        }
    }

    [Fact]
    public async Task Zadaci_koriste_samo_postojece_seme()
    {
        var set = await TestSet.UcitajAsync(PutanjaTestSeta());
        foreach (var z in set.Zadaci)
            Assert.Contains(z.Baza, new[] { "prodavnica", "fakultet" });
    }

    [Fact]
    public async Task Zadaci_imaju_jedinstvene_identifikatore()
    {
        var set = await TestSet.UcitajAsync(PutanjaTestSeta());
        Assert.Equal(set.Zadaci.Count, set.Zadaci.Select(z => z.Id).Distinct().Count());
    }
}
