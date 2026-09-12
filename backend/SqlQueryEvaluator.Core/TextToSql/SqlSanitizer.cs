using System.Text;
using System.Text.RegularExpressions;
using SqlQueryEvaluator.Core.Models;

namespace SqlQueryEvaluator.Core.TextToSql;

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

    private static readonly Regex RazmisljanjeZatvoreno =
        new(@"<think>.*?</think>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex RazmisljanjeNezatvoreno =
        new(@"<think>.*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    public static SanitizerResult Proveri(string? sirovSql, bool presecen) =>
        presecen
            ? SanitizerResult.Odbijen(
                "Model je dostigao granicu izlaznih tokena pre nego što je završio odgovor.")
            : Proveri(sirovSql);

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

        if (sql.Contains("$$") || Regex.IsMatch(sql, @"\$[a-zA-Z_]\w*\$"))
            return SanitizerResult.Odbijen("Upit sadrži dollar-quoted blok, što nije dozvoljeno.");

        var (bezKomentara, maskirano) = ObradiKomentareIStringove(sql);
        bezKomentara = bezKomentara.Trim();
        maskirano = maskirano.Trim();

        if (bezKomentara.Length == 0)
            return SanitizerResult.Odbijen("Posle uklanjanja komentara nije ostao nikakav upit.");

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

        if (Regex.IsMatch(maskirano, @"\b(pg_catalog|pg_shadow|pg_authid|pg_user|information_schema)\b",
                RegexOptions.IgnoreCase))
            return SanitizerResult.Odbijen("Pristup sistemskim katalozima nije dozvoljen.");

        return SanitizerResult.Ok(bezKomentara);
    }

    private static (string BezKomentara, string Maskirano) ObradiKomentareIStringove(string sql)
    {
        var cist = new StringBuilder(sql.Length);
        var maska = new StringBuilder(sql.Length);

        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];

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

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                cist.Append(' ');
                maska.Append(' ');
                continue;
            }

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
