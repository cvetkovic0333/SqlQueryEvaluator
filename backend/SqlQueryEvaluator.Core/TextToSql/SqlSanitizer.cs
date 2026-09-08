using System.Text;
using System.Text.RegularExpressions;
using SqlQueryEvaluator.Core.Models;

namespace SqlQueryEvaluator.Core.TextToSql;

/// <summary>
/// Drugi sloj zaštite pri izvršavanju SQL-a koji je generisao model.
/// (Prvi sloj je baza — rola sqleval_citanje sme samo SELECT.)
///
/// Postupak:
///   1. skidanje ```sql ograda koje modeli često dodaju uprkos uputstvu
///   2. uklanjanje komentara, uz maskiranje string literala — da se
///      ključne reči ne traže unutar podataka ('otkazana' nije DELETE)
///   3. dozvoljen je tačno JEDAN statement
///   4. mora da počinje sa SELECT ili WITH
///   5. odbijanje opasnih ključnih reči i funkcija
/// </summary>
public static class SqlSanitizer
{
    private static readonly string[] ZabranjeneReci =
    [
        "insert", "update", "delete", "drop", "alter", "create", "truncate",
        "grant", "revoke", "copy", "call", "do", "merge", "vacuum", "analyze",
        "reindex", "cluster", "listen", "notify", "prepare", "execute",
        "commit", "rollback", "savepoint", "begin", "set", "reset", "refresh",
        "comment", "lock", "import", "security"
    ];

    private static readonly string[] ZabranjeneFunkcije =
    [
        "pg_read_file", "pg_read_binary_file", "pg_ls_dir", "pg_sleep",
        "pg_stat_file", "lo_import", "lo_export", "dblink", "pg_terminate_backend",
        "pg_cancel_backend", "pg_reload_conf", "current_setting", "set_config",
        "query_to_xml", "pg_logdir_ls"
    ];

    private static readonly Regex Ograda =
        new(@"^\s*```[a-zA-Z]*\s*|\s*```\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Modeli koji "razmišljaju naglas" (Qwen3, DeepSeek-R1 i slični) ispisuju
    /// tok razmišljanja u &lt;think&gt; bloku pre samog odgovora. Taj blok se
    /// uklanja, inače bi upit počinjao tekstom umesto sa SELECT i bio odbijen
    /// iako je model dao ispravan SQL.
    /// </summary>
    private static readonly Regex RazmisljanjeZatvoreno =
        new(@"<think>.*?</think>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex RazmisljanjeNezatvoreno =
        new(@"<think>.*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    public static SanitizerResult Proveri(string? sirovSql)
    {
        if (string.IsNullOrWhiteSpace(sirovSql))
            return SanitizerResult.Odbijen("Model nije vratio nikakav SQL.");

        var bezRazmisljanja = RazmisljanjeZatvoreno.Replace(sirovSql, "");
        bezRazmisljanja = RazmisljanjeNezatvoreno.Replace(bezRazmisljanja, "");

        if (string.IsNullOrWhiteSpace(bezRazmisljanja))
            return SanitizerResult.Odbijen(
                "Model je vratio samo tok razmišljanja, bez SQL upita (verovatno je dostigao granicu tokena).");

        var sql = Ograda.Replace(bezRazmisljanja, "").Trim();

        // Dollar-quoting ($$...$$) nema šta da traži u SELECT upitu, a jeste
        // zgodan način da se sakrije sadržaj od provere — odbija se odmah.
        if (sql.Contains("$$") || Regex.IsMatch(sql, @"\$[a-zA-Z_]\w*\$"))
            return SanitizerResult.Odbijen("Upit sadrži dollar-quoted blok, što nije dozvoljeno.");

        var (bezKomentara, maskirano) = ObradiKomentareIStringove(sql);
        bezKomentara = bezKomentara.Trim();
        maskirano = maskirano.Trim();

        if (bezKomentara.Length == 0)
            return SanitizerResult.Odbijen("Posle uklanjanja komentara nije ostao nikakav upit.");

        // Tačno jedan statement: tačka-zarez sme samo na samom kraju.
        var tackaZarez = maskirano.IndexOf(';');
        if (tackaZarez >= 0 && maskirano[(tackaZarez + 1)..].Trim().Length > 0)
            return SanitizerResult.Odbijen("Dozvoljen je samo jedan SQL upit, a primljeno je više njih.");

        if (tackaZarez >= 0)
        {
            bezKomentara = bezKomentara[..bezKomentara.LastIndexOf(';')].TrimEnd();
            maskirano = maskirano[..tackaZarez].TrimEnd();
        }

        if (!Regex.IsMatch(maskirano, @"^\s*(select|with)\b", RegexOptions.IgnoreCase))
            return SanitizerResult.Odbijen("Dozvoljeni su samo upiti koji počinju sa SELECT ili WITH.");

        foreach (var rec in ZabranjeneReci)
        {
            if (Regex.IsMatch(maskirano, $@"\b{rec}\b", RegexOptions.IgnoreCase))
                return SanitizerResult.Odbijen($"Upit sadrži zabranjenu ključnu reč: {rec.ToUpperInvariant()}.");
        }

        foreach (var fun in ZabranjeneFunkcije)
        {
            if (maskirano.Contains(fun, StringComparison.OrdinalIgnoreCase))
                return SanitizerResult.Odbijen($"Upit sadrži zabranjenu funkciju: {fun}.");
        }

        // Sistemski katalozi ne služe filtriranju podataka, a otkrivaju
        // strukturu i korisnike baze.
        if (Regex.IsMatch(maskirano, @"\b(pg_catalog|pg_shadow|pg_authid|pg_user|information_schema)\b",
                RegexOptions.IgnoreCase))
            return SanitizerResult.Odbijen("Pristup sistemskim katalozima nije dozvoljen.");

        return SanitizerResult.Ok(bezKomentara);
    }

    /// <summary>
    /// Jedan prolaz kroz tekst koji istovremeno:
    ///   - izbacuje komentare (-- do kraja reda, /* */ sa ugnežđavanjem),
    ///   - vraća i verziju u kojoj je sadržaj string literala zamenjen
    ///     tačkama, da bi provera ključnih reči gledala samo strukturu upita.
    /// Bez maskiranja bi upit koji legitimno filtrira status = 'otkazana'
    /// bio odbijen zbog reči koja se nalazi u podacima, a ne u kodu.
    /// </summary>
    private static (string BezKomentara, string Maskirano) ObradiKomentareIStringove(string sql)
    {
        var cist = new StringBuilder(sql.Length);
        var maska = new StringBuilder(sql.Length);

        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];

            // Jednostruki navodnici — string literal.
            if (c == '\'')
            {
                cist.Append(c);
                maska.Append(c);
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        cist.Append("''");
                        maska.Append("..");
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '\'')
                    {
                        cist.Append('\'');
                        maska.Append('\'');
                        i++;
                        break;
                    }
                    cist.Append(sql[i]);
                    maska.Append('.');
                    i++;
                }
                continue;
            }

            // Dvostruki navodnici — citiran identifikator; ostaje kakav jeste.
            if (c == '"')
            {
                cist.Append(c);
                maska.Append(c);
                i++;
                while (i < sql.Length && sql[i] != '"')
                {
                    cist.Append(sql[i]);
                    maska.Append(sql[i]);
                    i++;
                }
                if (i < sql.Length) { cist.Append('"'); maska.Append('"'); i++; }
                continue;
            }

            // Linijski komentar.
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                cist.Append(' ');
                maska.Append(' ');
                continue;
            }

            // Blok komentar, sa ugnežđavanjem kao u PostgreSQL-u.
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var dubina = 1;
                i += 2;
                while (i < sql.Length && dubina > 0)
                {
                    if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*') { dubina++; i += 2; }
                    else if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/') { dubina--; i += 2; }
                    else i++;
                }
                cist.Append(' ');
                maska.Append(' ');
                continue;
            }

            cist.Append(c);
            maska.Append(c);
            i++;
        }

        return (cist.ToString(), maska.ToString());
    }
}
