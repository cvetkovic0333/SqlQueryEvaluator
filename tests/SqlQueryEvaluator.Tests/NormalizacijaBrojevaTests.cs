using SqlQueryEvaluator.Core.Evaluation;
using SqlQueryEvaluator.Core.Models;

namespace SqlQueryEvaluator.Tests;

public class NormalizacijaBrojevaTests
{
    private static QueryResult Jedna(object? v) => new()
    {
        Kolone = ["v"],
        Redovi = [[v]]
    };

    private static bool Poklapa(object? gold, object? gen) =>
        ExecutionAccuracyEvaluator.Uporedi(Jedna(gold), Jedna(gen), osetljivNaRedosled: false).Poklapa;

    [Fact]
    public void Prateće_nule_ne_prave_razliku()
    {
        Assert.True(Poklapa(12.30m, 12.3000000000000000m));
    }

    [Fact]
    public void Ceo_broj_i_numeric_sa_nulama_su_isti()
    {
        Assert.True(Poklapa(42L, 42.00m));
    }

    [Fact]
    public void Decimal_i_double_su_isti_na_cetiri_decimale()
    {
        Assert.True(Poklapa(1234.5678m, 1234.56781234));
    }

    [Fact]
    public void Stvarno_razliciti_brojevi_ostaju_razliciti()
    {
        Assert.False(Poklapa(12.30m, 12.31m));
        Assert.False(Poklapa(12.3m, 12.2487m));
    }

    private static QueryResult Rezultat(string[] kolone, params object?[][] redovi) => new()
    {
        Kolone = [.. kolone],
        Redovi = [.. redovi.Select(r => r.ToList())]
    };

    [Fact]
    public void Nezaokruzen_prosek_se_poklapa_sa_prosekom_zaokruzenim_u_tacnom_upitu()
    {
        var gold = Rezultat(["naziv", "prosek"], ["Asus", 4.86m], ["Dell", 3.50m]);
        var gen = Rezultat(["naziv", "prosecna_ocena"], ["Dell", 3.5000000000000000m], ["Asus", 4.8571428571428571m]);

        var ishod = ExecutionAccuracyEvaluator.Uporedi(gold, gen, osetljivNaRedosled: false);
        Assert.True(ishod.Poklapa, ishod.Objasnjenje);
        Assert.Contains("zaokružene su na 2 decimale", ishod.Objasnjenje);
    }

    [Fact]
    public void Zaokruzivanje_prati_broj_decimala_iz_tacnog_upita()
    {
        Assert.True(Poklapa(85.4m, 85.4321m));
        Assert.True(Poklapa(7.11m, 7.1120879120879121));
    }

    [Fact]
    public void Polovina_se_zaokruzuje_navise_kao_u_postgresql_u()
    {
        Assert.True(Poklapa(4.13m, 4.125m));
        Assert.True(Poklapa(-4.13m, -4.125m));
    }

    [Fact]
    public void Pogresan_prosek_ostaje_greska_i_posle_zaokruzivanja()
    {
        Assert.False(Poklapa(4.86m, 4.8449m));
    }

    [Fact]
    public void Grublje_zaokruzivanje_od_tacnog_upita_je_greska()
    {
        Assert.False(Poklapa(4.86m, 4.9m));
    }

    [Fact]
    public void Celobrojna_kolona_tacnog_upita_se_ne_zaokruzuje()
    {
        Assert.False(Poklapa(3L, 2.6m));
    }

    private static bool PoklapaUzTrazenoZaokruzivanje(object? gold, object? gen) =>
        ExecutionAccuracyEvaluator.Uporedi(Jedna(gold), Jedna(gen), osetljivNaRedosled: false, zaokruzivanjeTrazeno: true).Poklapa;

    [Fact]
    public void Kada_pitanje_trazi_zaokruzivanje_vise_decimala_je_greska()
    {
        Assert.False(PoklapaUzTrazenoZaokruzivanje(45.3m, 45.2876m));
        Assert.False(PoklapaUzTrazenoZaokruzivanje(45.3m, 45.29m));
    }

    [Fact]
    public void Kada_pitanje_trazi_zaokruzivanje_manje_decimala_je_greska()
    {
        Assert.False(PoklapaUzTrazenoZaokruzivanje(45.3m, 45m));
    }

    [Fact]
    public void Kada_pitanje_trazi_zaokruzivanje_ispravno_zaokruzen_broj_je_tacan()
    {
        Assert.True(PoklapaUzTrazenoZaokruzivanje(45.3m, 45.30m));
    }

    [Fact]
    public void Zaokruzivanje_vazi_i_kada_model_vrati_kolonu_vise_ili_manje()
    {
        var gold = Rezultat(["naziv", "prosek", "broj"], ["Asus", 4.86m, 21L], ["Dell", 3.50m, 8L]);
        var visak = Rezultat(["id", "naziv", "prosek", "broj"],
            [1, "Asus", 4.8571428571428571m, 21L], [2, "Dell", 3.5m, 8L]);
        var manjak = Rezultat(["naziv", "prosek"], ["Asus", 4.8571428571428571m], ["Dell", 3.5m]);

        Assert.True(ExecutionAccuracyEvaluator.Uporedi(gold, visak, false).Poklapa);
        Assert.True(ExecutionAccuracyEvaluator.Uporedi(gold, manjak, false).Poklapa);
    }
}
