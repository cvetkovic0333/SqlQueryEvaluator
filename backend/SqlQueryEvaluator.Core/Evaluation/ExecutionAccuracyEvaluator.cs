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

        var kolonePoGold = Kolone(gold);
        var kolonePoGen = Kolone(generisano);

        var goldJeUzi = kolonePoGold.Count <= kolonePoGen.Count;
        var uzi = goldJeUzi ? kolonePoGold : kolonePoGen;
        var siri = goldJeUzi ? kolonePoGen : kolonePoGold;
        var imenaUzih = goldJeUzi ? gold.Kolone : generisano.Kolone;

        var kandidati = new List<List<int>>(uzi.Count);
        for (var i = 0; i < uzi.Count; i++)
        {
            var moguce = Enumerable.Range(0, siri.Count)
                .Where(j => osetljivNaRedosled
                    ? uzi[i].SequenceEqual(siri[j])
                    : IstiMultiskup(uzi[i], siri[j]))
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

        var redovaUzih = uzi.Count == 0 ? 0 : uzi[0].Count;
        var uparivanje = new int[uzi.Count];
        var zauzete = new bool[siri.Count];
        var pokusaja = 0;

        bool Upari(int i)
        {
            if (i == uzi.Count)
                return osetljivNaRedosled || IsteProjekcije(uzi, siri, uparivanje, redovaUzih);

            var probane = new List<int>();
            foreach (var j in kandidati[i])
            {
                if (zauzete[j] || probane.Any(p => siri[p].SequenceEqual(siri[j])))
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

        return Ishod(true, (osetljivNaRedosled
            ? "Rezultati se poklapaju, uključujući redosled redova."
            : "Rezultati se poklapaju (redosled nije bio bitan).") + napomena);
    }

    private static List<List<string>> Kolone(QueryResult rezultat) =>
        Enumerable.Range(0, rezultat.Kolone.Count)
            .Select(k => rezultat.Redovi
                .Select(red => k < red.Count ? NormalizujVrednost(red[k]) : NormalizujVrednost(null))
                .ToList())
            .ToList();

    private static bool IstiMultiskup(List<string> a, List<string> b) =>
        a.Count == b.Count && a.Order(StringComparer.Ordinal).SequenceEqual(b.Order(StringComparer.Ordinal));

    private static bool IsteProjekcije(
        List<List<string>> uzi, List<List<string>> siri, int[] uparivanje, int redova)
    {
        var brojaci = new Dictionary<string, int>();
        for (var r = 0; r < redova; r++)
        {
            var kljuc = string.Join("␟", uzi.Select(kolona => kolona[r]));
            brojaci[kljuc] = brojaci.GetValueOrDefault(kljuc) + 1;
        }

        for (var r = 0; r < redova; r++)
        {
            var kljuc = string.Join("␟", uparivanje.Select(j => siri[j][r]));
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

    private const string FormatBroja = "0.####";

    internal static string NormalizujVrednost(object? v) => v switch
    {
        null => "␀NULL",
        decimal d => Math.Round(d, 4).ToString(FormatBroja, CultureInfo.InvariantCulture),
        double d => Math.Round(d, 4).ToString(FormatBroja, CultureInfo.InvariantCulture),
        float f => Math.Round((double)f, 4).ToString(FormatBroja, CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => s.Trim(),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? ""
    };

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
