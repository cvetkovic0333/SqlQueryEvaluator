using SqlQueryEvaluator.Core.Evaluation;
using SqlQueryEvaluator.Core.Models;

namespace SqlQueryEvaluator.Tests;

/// <summary>
/// Isti broj zapisan sa različitim brojem decimala ili različitim tipom
/// mora da se poklopi — inače bi tačan upit bio proglašen netačnim samo
/// zato što nije zaokružio na isti broj mesta kao gold upit.
/// </summary>
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
        // double stiže kada je numeric prevelik za .NET decimal.
        Assert.True(Poklapa(1234.5678m, 1234.56781234));
    }

    [Fact]
    public void Stvarno_razliciti_brojevi_ostaju_razliciti()
    {
        Assert.False(Poklapa(12.30m, 12.31m));
        Assert.False(Poklapa(12.3m, 12.2987m));
    }
}
