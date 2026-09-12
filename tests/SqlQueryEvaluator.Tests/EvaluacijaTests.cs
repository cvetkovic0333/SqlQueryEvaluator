using SqlQueryEvaluator.Core.Evaluation;
using SqlQueryEvaluator.Core.Models;

namespace SqlQueryEvaluator.Tests;

public class ExecutionAccuracyTests
{
    private static QueryResult Rezultat(string[] kolone, params object?[][] redovi) => new()
    {
        Kolone = [.. kolone],
        Redovi = [.. redovi.Select(r => r.ToList())]
    };

    [Fact]
    public void Isti_redovi_u_drugom_redosledu_se_poklapaju_kada_gold_nema_order_by()
    {
        var gold = Rezultat(["grad", "broj"], ["Niš", 10], ["Beograd", 20]);
        var gen = Rezultat(["a", "b"], ["Beograd", 20], ["Niš", 10]);

        var ishod = ExecutionAccuracyEvaluator.Uporedi(gold, gen, osetljivNaRedosled: false);
        Assert.True(ishod.Poklapa, ishod.Objasnjenje);
    }

    [Fact]
    public void Drugi_redosled_je_greska_kada_gold_ima_order_by()
    {
        var gold = Rezultat(["grad"], ["Beograd"], ["Niš"]);
        var gen = Rezultat(["grad"], ["Niš"], ["Beograd"]);

        Assert.False(ExecutionAccuracyEvaluator.Uporedi(gold, gen, osetljivNaRedosled: true).Poklapa);
    }

    [Fact]
    public void Imena_kolona_se_ignorisu()
    {
        var gold = Rezultat(["ukupno"], [42]);
        var gen = Rezultat(["sum"], [42]);

        Assert.True(ExecutionAccuracyEvaluator.Uporedi(gold, gen, false).Poklapa);
    }

    [Fact]
    public void Razlicit_broj_kolona_je_greska()
    {
        var gold = Rezultat(["a"], [1]);
        var gen = Rezultat(["a", "b"], [1, 2]);

        var ishod = ExecutionAccuracyEvaluator.Uporedi(gold, gen, false);
        Assert.False(ishod.Poklapa);
        Assert.Contains("kolona", ishod.Objasnjenje);
    }

    [Fact]
    public void Razlicit_broj_redova_je_greska()
    {
        var gold = Rezultat(["a"], [1], [2]);
        var gen = Rezultat(["a"], [1]);

        Assert.False(ExecutionAccuracyEvaluator.Uporedi(gold, gen, false).Poklapa);
    }

    [Fact]
    public void Duplikati_se_racunaju_kao_multiskup()
    {
        var gold = Rezultat(["a"], [1], [1], [2]);
        var gen = Rezultat(["a"], [1], [2], [2]);

        Assert.False(ExecutionAccuracyEvaluator.Uporedi(gold, gen, false).Poklapa);
    }

    [Fact]
    public void Brojevi_se_porede_zaokruzeni_na_cetiri_decimale()
    {
        var gold = Rezultat(["prosek"], [3.14159265m]);
        var gen = Rezultat(["prosek"], [3.14159300m]);

        Assert.True(ExecutionAccuracyEvaluator.Uporedi(gold, gen, false).Poklapa);
    }

    [Fact]
    public void Null_se_razlikuje_od_praznog_teksta()
    {
        var gold = Rezultat(["v"], [(object?)null]);
        var gen = Rezultat(["v"], [""]);

        Assert.False(ExecutionAccuracyEvaluator.Uporedi(gold, gen, false).Poklapa);
    }

    [Fact]
    public void Vrednosti_iz_susednih_kolona_ne_mogu_da_se_preliju()
    {
        var gold = Rezultat(["x", "y"], ["ab", "c"]);
        var gen = Rezultat(["x", "y"], ["a", "bc"]);

        Assert.False(ExecutionAccuracyEvaluator.Uporedi(gold, gen, false).Poklapa);
    }

    [Theory]
    [InlineData("SELECT a FROM t ORDER BY a", true)]
    [InlineData("SELECT a FROM t", false)]
    [InlineData("SELECT rank() OVER (ORDER BY a) FROM t", false)]
    [InlineData("SELECT rank() OVER (ORDER BY a) FROM t ORDER BY b", true)]
    public void Prepoznaje_pravi_order_by_a_ne_onaj_u_window_funkciji(string sql, bool ocekivano)
    {
        Assert.Equal(ocekivano, ExecutionAccuracyEvaluator.ImaOrderBy(sql));
    }
}

public class MetricsCalculatorTests
{
    private static RezultatZaMetriku R(bool rezultatIsti, bool? sudijaTacan, int ocena = 5) =>
        new("m", "lak", "sr", true, rezultatIsti, ocena, sudijaTacan, 100, 10, 10);

    [Fact]
    public void Kappa_je_jedan_kada_se_sudija_potpuno_slaze()
    {
        var redovi = new[]
        {
            R(true, true), R(true, true), R(false, false), R(false, false)
        };

        var s = MetricsCalculator.IzracunajSlaganje(redovi);
        Assert.Equal(100, s.Slaganje);
        Assert.Equal(1.0, s.Kappa, 3);
    }

    [Fact]
    public void Kappa_je_nula_kada_sudija_uvek_kaze_tacno()
    {
        var redovi = new[]
        {
            R(true, true), R(true, true), R(true, true), R(false, true)
        };

        var s = MetricsCalculator.IzracunajSlaganje(redovi);
        Assert.Equal(75, s.Slaganje);
        Assert.Equal(0.0, s.Kappa, 3);
    }

    [Fact]
    public void Broji_lazno_pozitivne_i_lazno_negativne()
    {
        var redovi = new[]
        {
            R(true, true),
            R(false, true),
            R(true, false),
            R(false, false)
        };

        var s = MetricsCalculator.IzracunajSlaganje(redovi);
        Assert.Equal(1, s.LaznoPozitivno);
        Assert.Equal(1, s.LaznoNegativno);
        Assert.Equal(50, s.Slaganje);
    }

    [Fact]
    public void Metrike_grupe_racunaju_procente()
    {
        var redovi = new[] { R(true, true), R(true, true), R(false, false), R(false, false) };

        var m = MetricsCalculator.ZaGrupu("test", redovi);
        Assert.Equal(4, m.Broj);
        Assert.Equal(50, m.ExecutionAccuracy);
        Assert.Equal(100, m.ValidSqlRate);
    }

    [Fact]
    public void Prazna_grupa_ne_puca()
    {
        var m = MetricsCalculator.ZaGrupu("prazno", []);
        Assert.Equal(0, m.Broj);
        Assert.Equal(0, m.ExecutionAccuracy);
    }
}

public class LlmJudgeTests
{
    [Fact]
    public void Rasclanjuje_cist_json()
    {
        var o = LlmJudge.Rasclani("""{"ocena": 5, "tacan": true, "obrazlozenje": "Sve je u redu."}""", "m");
        Assert.Equal(5, o.Ocena);
        Assert.True(o.Tacan);
        Assert.Equal("Sve je u redu.", o.Obrazlozenje);
    }

    [Fact]
    public void Izvlaci_json_iz_markdown_ograde()
    {
        var o = LlmJudge.Rasclani("```json\n{\"ocena\": 3, \"tacan\": false, \"obrazlozenje\": \"Fali filter.\"}\n```", "m");
        Assert.Equal(3, o.Ocena);
        Assert.False(o.Tacan);
    }

    [Fact]
    public void Ocena_ispod_cetiri_povlaci_tacan_na_false()
    {
        var o = LlmJudge.Rasclani("""{"ocena": 2, "tacan": true, "obrazlozenje": "x"}""", "m");
        Assert.False(o.Tacan);
    }

    [Fact]
    public void Ogranicava_ocenu_na_opseg_jedan_do_pet()
    {
        Assert.Equal(5, LlmJudge.Rasclani("""{"ocena": 9, "tacan": true}""", "m").Ocena);
        Assert.Equal(1, LlmJudge.Rasclani("""{"ocena": -3, "tacan": false}""", "m").Ocena);
    }

    [Fact]
    public void Prihvata_ocenu_zapisanu_kao_tekst()
    {
        var o = LlmJudge.Rasclani("""{"ocena": "4", "tacan": "true", "obrazlozenje": "ok"}""", "m");
        Assert.Equal(4, o.Ocena);
        Assert.True(o.Tacan);
    }

    [Fact]
    public void Vraca_gresku_kada_odgovor_nije_json()
    {
        var o = LlmJudge.Rasclani("Izvinite, ne mogu da ocenim ovaj upit.", "m");
        Assert.Equal(0, o.Ocena);
        Assert.False(o.Tacan);
        Assert.Contains("nije uspeo", o.Obrazlozenje);
    }

    [Fact]
    public void Spasava_ocenu_iz_presecenog_json_a()
    {
        var o = LlmJudge.Rasclani(
            """{"ocena": 4, "tacan": true, "obrazlozenje": "Upit je logic""", "m");

        Assert.Equal(4, o.Ocena);
        Assert.True(o.Tacan);
        Assert.Contains("Upit je logic", o.Obrazlozenje);
    }

    [Fact]
    public void Ne_spasava_nista_kada_ni_ocene_nema()
    {
        var o = LlmJudge.Rasclani("{\"nesto\": \"drugo\"", "m");
        Assert.Equal(0, o.Ocena);
    }
}
