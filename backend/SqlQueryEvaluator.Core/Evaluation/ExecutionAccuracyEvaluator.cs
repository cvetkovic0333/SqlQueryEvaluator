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

/// <summary>
/// Execution accuracy — objektivna metrika kvaliteta.
///
/// Izvršava se i gold i generisani SQL nad istom bazom, pa se porede
/// REZULTATI, a ne tekst upita. Razlog: isto pitanje ima više ispravnih
/// formulacija (JOIN vs podupit, drugačiji redosled kolona), pa bi
/// poređenje teksta kažnjavalo tačne odgovore.
///
/// Pravila poređenja:
///   - redovi se porede kao multiskup (redosled se ignoriše), OSIM kada
///     gold upit ima ORDER BY — tada je redosled deo tačnog odgovora;
///   - imena kolona se ignorišu (model sme da nazove kolonu kako hoće),
///     ali broj kolona mora da se poklopi;
///   - brojevi se zaokružuju na 4 decimale, tekst se trimuje.
/// </summary>
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

    internal static IshodPoredjenja Uporedi(QueryResult gold, QueryResult generisano, bool osetljivNaRedosled)
    {
        if (gold.Kolone.Count != generisano.Kolone.Count)
            return new IshodPoredjenja(false,
                $"Različit broj kolona: očekivano {gold.Kolone.Count}, dobijeno {generisano.Kolone.Count}.",
                gold.BrojRedova, generisano.BrojRedova);

        if (gold.BrojRedova != generisano.BrojRedova)
            return new IshodPoredjenja(false,
                $"Različit broj redova: očekivano {gold.BrojRedova}, dobijeno {generisano.BrojRedova}.",
                gold.BrojRedova, generisano.BrojRedova);

        var redoviGold = gold.Redovi.Select(NormalizujRed).ToList();
        var redoviGen = generisano.Redovi.Select(NormalizujRed).ToList();

        if (osetljivNaRedosled)
        {
            for (var i = 0; i < redoviGold.Count; i++)
            {
                if (redoviGold[i] != redoviGen[i])
                    return new IshodPoredjenja(false,
                        $"Red {i + 1} se razlikuje (gold upit ima ORDER BY, pa je redosled bitan).",
                        gold.BrojRedova, generisano.BrojRedova);
            }

            return new IshodPoredjenja(true, "Rezultati su identični, uključujući redosled redova.",
                gold.BrojRedova, generisano.BrojRedova);
        }

        var brojaciGold = redoviGold.GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
        var brojaciGen = redoviGen.GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());

        foreach (var (red, koliko) in brojaciGold)
        {
            if (!brojaciGen.TryGetValue(red, out var koliko2) || koliko != koliko2)
                return new IshodPoredjenja(false,
                    "Skupovi redova se razlikuju iako je broj redova isti.",
                    gold.BrojRedova, generisano.BrojRedova);
        }

        return new IshodPoredjenja(true, "Rezultati se poklapaju (redosled nije bio bitan).",
            gold.BrojRedova, generisano.BrojRedova);
    }

    /// <summary>
    /// Red se svodi na tekst sa razdvajačem koji se ne pojavljuje u podacima,
    /// da vrednosti iz susednih kolona ne bi slučajno "iscurele" jedna u drugu.
    /// </summary>
    private static string NormalizujRed(List<object?> red) =>
        string.Join("␟", red.Select(NormalizujVrednost));

    /// <summary>
    /// Brojevi se ispisuju formatom "0.####" — bez pratećih nula. decimal
    /// pamti broj decimala, pa bi inače 12.30 (numeric(10,2) iz gold upita)
    /// i 12.3000 (AVG bez zaokruživanja) bili različit tekst iako su isti
    /// broj, a isto i 42 (bigint) naspram 42.00 (numeric).
    /// </summary>
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

    /// <summary>
    /// Traži se ORDER BY koji pripada samom upitu, a ne onaj unutar window
    /// funkcije (OVER (ORDER BY ...)) — taj ne određuje redosled rezultata.
    /// </summary>
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
