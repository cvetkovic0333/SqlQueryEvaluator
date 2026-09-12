namespace SqlQueryEvaluator.Core.Evaluation;

public sealed record RezultatZaMetriku(
    string ModelId,
    string Tezina,
    string Jezik,
    bool SqlIspravan,
    bool RezultatIsti,
    int? OcenaSudije,
    bool? SudijaTacan,
    int TrajanjeMs,
    int UlazniTokeni,
    int IzlazniTokeni);

public sealed record Metrike(
    string Kljuc,
    int Broj,
    double ValidSqlRate,
    double ExecutionAccuracy,
    double ProsecnaOcenaSudije,
    double ProsecnoTrajanjeMs,
    double ProsecnoTokena);

public sealed record SlaganjeSudije(
    double Slaganje,
    double Kappa,
    int TacnoPozitivno,
    int TacnoNegativno,
    int LaznoPozitivno,
    int LaznoNegativno);

public static class MetricsCalculator
{
    public static Metrike ZaGrupu(string kljuc, IReadOnlyCollection<RezultatZaMetriku> redovi)
    {
        if (redovi.Count == 0)
            return new Metrike(kljuc, 0, 0, 0, 0, 0, 0);

        var saOcenom = redovi.Where(r => r.OcenaSudije is > 0).ToList();

        return new Metrike(
            kljuc,
            redovi.Count,
            Procenat(redovi.Count(r => r.SqlIspravan), redovi.Count),
            Procenat(redovi.Count(r => r.RezultatIsti), redovi.Count),
            saOcenom.Count == 0 ? 0 : Math.Round(saOcenom.Average(r => r.OcenaSudije!.Value), 2),
            Math.Round(redovi.Average(r => (double)r.TrajanjeMs), 0),
            Math.Round(redovi.Average(r => (double)(r.UlazniTokeni + r.IzlazniTokeni)), 0));
    }

    public static Dictionary<string, Metrike> PoModelu(IEnumerable<RezultatZaMetriku> redovi) =>
        redovi.GroupBy(r => r.ModelId).ToDictionary(g => g.Key, g => ZaGrupu(g.Key, g.ToList()));

    public static Dictionary<string, Metrike> PoTezini(IEnumerable<RezultatZaMetriku> redovi) =>
        redovi.GroupBy(r => r.Tezina).ToDictionary(g => g.Key, g => ZaGrupu(g.Key, g.ToList()));

    public static Dictionary<string, Metrike> PoJeziku(IEnumerable<RezultatZaMetriku> redovi) =>
        redovi.GroupBy(r => r.Jezik).ToDictionary(g => g.Key, g => ZaGrupu(g.Key, g.ToList()));

    public static SlaganjeSudije IzracunajSlaganje(IEnumerable<RezultatZaMetriku> redovi)
    {
        var uzorak = redovi.Where(r => r.SudijaTacan.HasValue).ToList();
        if (uzorak.Count == 0)
            return new SlaganjeSudije(0, 0, 0, 0, 0, 0);

        var tp = uzorak.Count(r => r.SudijaTacan!.Value && r.RezultatIsti);
        var tn = uzorak.Count(r => !r.SudijaTacan!.Value && !r.RezultatIsti);
        var fp = uzorak.Count(r => r.SudijaTacan!.Value && !r.RezultatIsti);
        var fn = uzorak.Count(r => !r.SudijaTacan!.Value && r.RezultatIsti);

        var n = (double)uzorak.Count;
        var posmatrano = (tp + tn) / n;

        var sudijaDa = (tp + fp) / n;
        var sudijaNe = (tn + fn) / n;
        var stvarnoDa = (tp + fn) / n;
        var stvarnoNe = (tn + fp) / n;
        var ocekivano = sudijaDa * stvarnoDa + sudijaNe * stvarnoNe;

        var kappa = Math.Abs(1 - ocekivano) < 1e-9 ? 1.0 : (posmatrano - ocekivano) / (1 - ocekivano);

        return new SlaganjeSudije(
            Math.Round(posmatrano * 100, 1),
            Math.Round(kappa, 3),
            tp, tn, fp, fn);
    }

    public static string OpisKappe(double kappa) => kappa switch
    {
        < 0.00 => "gore od slučajnog pogađanja",
        < 0.20 => "zanemarljivo slaganje",
        < 0.40 => "slabo slaganje",
        < 0.60 => "umereno slaganje",
        < 0.80 => "znatno slaganje",
        _ => "gotovo potpuno slaganje"
    };

    private static double Procenat(int deo, int celina) =>
        celina == 0 ? 0 : Math.Round(100.0 * deo / celina, 1);
}
