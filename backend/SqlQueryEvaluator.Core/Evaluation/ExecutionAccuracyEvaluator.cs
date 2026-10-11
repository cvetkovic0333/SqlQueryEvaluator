using System.Globalization;
using System.Text.RegularExpressions;
using SqlQueryEvaluator.Core.Models;
using SqlQueryEvaluator.Core.TextToSql;

namespace SqlQueryEvaluator.Core.Evaluation;

public sealed record IshodPoredjenja(
    bool Poklapa,
    string Objasnjenje,
    int RedovaGold,
    int RedovaGenerisano);

public sealed class ExecutionAccuracyEvaluator(QueryExecutor izvrsilac)
{
    private const int MaxRedovaZaPoredjenje = 5000;

    public async Task<IshodPoredjenja> UporediAsync(
        string goldSql, string generisaniSql, CancellationToken ct = default)
    {
        var gold = await izvrsilac.IzvrsiAsync(goldSql, MaxRedovaZaPoredjenje, ct);
        if (!gold.Uspesno)
            return new IshodPoredjenja(false, $"Gold SQL se nije izvršio: {gold.Greska}", 0, 0);

        var generisano = await izvrsilac.IzvrsiAsync(generisaniSql, MaxRedovaZaPoredjenje, ct);
        if (!generisano.Uspesno)
            return new IshodPoredjenja(false, $"Generisani SQL se nije izvršio: {generisano.Greska}",
                gold.BrojRedova, 0);

        var osetljivNaRedosled = ImaOrderBy(goldSql);
        return Uporedi(gold, generisano, osetljivNaRedosled);
    }

    private const int MaxPokusajaUparivanja = 100_000;

    internal static IshodPoredjenja Uporedi(QueryResult gold, QueryResult generisano, bool osetljivNaRedosled)
    {
        IshodPoredjenja Ishod(bool poklapa, string objasnjenje) =>
            new(poklapa, objasnjenje, gold.BrojRedova, generisano.BrojRedova);

        if (generisano.Kolone.Count == 0)
            return Ishod(false, "Generisani upit nije vratio nijednu kolonu.");

        if (gold.BrojRedova != generisano.BrojRedova)
            return Ishod(false,
                $"Različit broj redova: očekivano {gold.BrojRedova}, dobijeno {generisano.BrojRedova}.");

        var kolonePoGold = Kolone(gold, null);
        var decimaleGold = DecimaleKolona(gold);
        var kolonePoGen = new Dictionary<int, List<List<string>>>();

        List<string> Generisana(int kolona, int? decimale)
        {
            var kljuc = decimale ?? -1;
            if (!kolonePoGen.TryGetValue(kljuc, out var kolone))
                kolonePoGen[kljuc] = kolone = Kolone(generisano, decimale);
            return kolone[kolona];
        }

        var goldJeUzi = gold.Kolone.Count <= generisano.Kolone.Count;
        var brojUzih = goldJeUzi ? gold.Kolone.Count : generisano.Kolone.Count;
        var brojSirih = goldJeUzi ? generisano.Kolone.Count : gold.Kolone.Count;
        var imenaUzih = goldJeUzi ? gold.Kolone : generisano.Kolone;

        (List<string> Uza, List<string> Sira) Par(int u, int s) => goldJeUzi
            ? (kolonePoGold[u], Generisana(s, decimaleGold[u]))
            : (Generisana(u, decimaleGold[s]), kolonePoGold[s]);

        var kandidati = new List<List<int>>(brojUzih);
        for (var i = 0; i < brojUzih; i++)
        {
            var moguce = Enumerable.Range(0, brojSirih)
                .Where(j => osetljivNaRedosled
                    ? Par(i, j).Uza.SequenceEqual(Par(i, j).Sira)
                    : IstiMultiskup(Par(i, j).Uza, Par(i, j).Sira))
                .ToList();

            if (moguce.Count == 0)
            {
                var izvor = goldJeUzi ? "gold rezultatu" : "generisanom rezultatu";
                var drugi = goldJeUzi ? "generisanom" : "gold";
                return Ishod(false,
                    $"Podaci se ne poklapaju: za kolonu „{imenaUzih[i]}” u {izvor} " +
                    $"ne postoji kolona sa istim vrednostima u {drugi} rezultatu.");
            }

            kandidati.Add(moguce);
        }

        var uparivanje = new int[brojUzih];
        var zauzete = new bool[brojSirih];
        var pokusaja = 0;

        bool Upari(int i)
        {
            if (i == brojUzih)
                return osetljivNaRedosled || IsteProjekcije(
                    Enumerable.Range(0, brojUzih).Select(u => Par(u, uparivanje[u])).ToList(), gold.BrojRedova);

            var probane = new List<int>();
            foreach (var j in kandidati[i])
            {
                if (zauzete[j] || probane.Any(p =>
                        Par(i, p).Uza.SequenceEqual(Par(i, j).Uza) && Par(i, p).Sira.SequenceEqual(Par(i, j).Sira)))
                    continue;
                if (++pokusaja > MaxPokusajaUparivanja)
                    return false;

                probane.Add(j);
                zauzete[j] = true;
                uparivanje[i] = j;
                if (Upari(i + 1))
                    return true;
                zauzete[j] = false;
            }

            return false;
        }

        if (!Upari(0))
            return Ishod(false, osetljivNaRedosled
                ? "Redovi se razlikuju (gold upit ima ORDER BY, pa je redosled bitan)."
                : "Skupovi redova se razlikuju iako je broj redova isti.");

        var razlika = generisano.Kolone.Count - gold.Kolone.Count;
        var napomena = razlika switch
        {
            > 0 => $" Generisani upit ima {razlika} kolon{Nastavak(razlika)} više, što se ne kažnjava.",
            < 0 => $" Generisani upit ima {-razlika} kolon{Nastavak(-razlika)} manje, što se ne kažnjava.",
            _ => ""
        };

        var zaokruzeno = Enumerable.Range(0, brojUzih)
            .Select(u => goldJeUzi ? (Gold: u, Gen: uparivanje[u]) : (Gold: uparivanje[u], Gen: u))
            .Where(p => decimaleGold[p.Gold] is { } d
                && !Generisana(p.Gen, d).SequenceEqual(Generisana(p.Gen, null)))
            .Select(p => decimaleGold[p.Gold]!.Value)
            .Distinct()
            .Order()
            .ToList();
        if (zaokruzeno.Count > 0)
            napomena += $" Vrednosti generisanog upita zaokružene su na {string.Join(" i ", zaokruzeno)} " +
                $"decimal{Nastavak(zaokruzeno[^1])} kao u tačnom upitu, što se ne kažnjava.";

        return Ishod(true, (osetljivNaRedosled
            ? "Rezultati se poklapaju, uključujući redosled redova."
            : "Rezultati se poklapaju (redosled nije bio bitan).") + napomena);
    }

    private static List<List<string>> Kolone(QueryResult rezultat, int? decimale) =>
        Enumerable.Range(0, rezultat.Kolone.Count)
            .Select(k => rezultat.Redovi
                .Select(red => NormalizujVrednost(k < red.Count ? red[k] : null, decimale))
                .ToList())
            .ToList();

    private static int?[] DecimaleKolona(QueryResult rezultat) =>
        Enumerable.Range(0, rezultat.Kolone.Count).Select(k => DecimaleKolone(rezultat, k)).ToArray();

    private static int? DecimaleKolone(QueryResult rezultat, int kolona)
    {
        int? najvise = null;
        foreach (var red in rezultat.Redovi)
        {
            switch (kolona < red.Count ? red[kolona] : null)
            {
                case null:
                    continue;
                case decimal d:
                    najvise = Math.Max(najvise ?? 0, d.Scale);
                    break;
                default:
                    return null;
            }
        }

        return najvise < Decimale ? najvise : null;
    }

    private static bool IstiMultiskup(List<string> a, List<string> b) =>
        a.Count == b.Count && a.Order(StringComparer.Ordinal).SequenceEqual(b.Order(StringComparer.Ordinal));

    private static bool IsteProjekcije(List<(List<string> Uza, List<string> Sira)> parovi, int redova)
    {
        var brojaci = new Dictionary<string, int>();
        for (var r = 0; r < redova; r++)
        {
            var kljuc = string.Join("␟", parovi.Select(p => p.Uza[r]));
            brojaci[kljuc] = brojaci.GetValueOrDefault(kljuc) + 1;
        }

        for (var r = 0; r < redova; r++)
        {
            var kljuc = string.Join("␟", parovi.Select(p => p.Sira[r]));
            if (!brojaci.TryGetValue(kljuc, out var koliko) || koliko == 0)
                return false;
            brojaci[kljuc] = koliko - 1;
        }

        return true;
    }

    private static string Nastavak(int broj) =>
        broj % 10 == 1 && broj % 100 != 11 ? "u"
        : broj % 10 is >= 2 and <= 4 && broj % 100 is < 12 or > 14 ? "e"
        : "a";

    private const int Decimale = 4;
    private const string FormatBroja = "0.####";

    internal static string NormalizujVrednost(object? v, int? decimale = null) => v switch
    {
        null => "␀NULL",
        decimal d => Zaokruzi(d, decimale).ToString(FormatBroja, CultureInfo.InvariantCulture),
        double d => Zaokruzi(d, decimale).ToString(FormatBroja, CultureInfo.InvariantCulture),
        float f => Zaokruzi((double)f, decimale).ToString(FormatBroja, CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => s.Trim(),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? ""
    };

    private static decimal Zaokruzi(decimal d, int? decimale) => decimale is { } n
        ? Math.Round(d, n, MidpointRounding.AwayFromZero)
        : Math.Round(d, Decimale);

    private static double Zaokruzi(double d, int? decimale) => decimale is { } n
        ? Math.Round(d, n, MidpointRounding.AwayFromZero)
        : Math.Round(d, Decimale);

    internal static bool ImaOrderBy(string sql)
    {
        var bezZagrada = UkloniZagrade(sql);
        return Regex.IsMatch(bezZagrada, @"\border\s+by\b", RegexOptions.IgnoreCase);
    }

    private static string UkloniZagrade(string sql)
    {
        var rezultat = new System.Text.StringBuilder(sql.Length);
        var dubina = 0;
        foreach (var c in sql)
        {
            if (c == '(') dubina++;
            else if (c == ')') dubina = Math.Max(0, dubina - 1);
            else if (dubina == 0) rezultat.Append(c);
        }
        return rezultat.ToString();
    }
}
