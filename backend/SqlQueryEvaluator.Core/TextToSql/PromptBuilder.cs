using System.Text;
using SqlQueryEvaluator.Core.Models;

namespace SqlQueryEvaluator.Core.TextToSql;

public static class PromptBuilder
{
    public const string SistemskiPrompt = """
        You are an expert PostgreSQL analyst. Translate the user's question into
        exactly ONE PostgreSQL query.

        HARD RULES:
        1. Output ONLY the SQL query. No explanation, no comments, no markdown fences.
        2. The query must be read-only: it must start with SELECT or WITH.
           Never emit INSERT, UPDATE, DELETE, DROP, ALTER, CREATE, TRUNCATE or GRANT.
        3. Use only the tables and columns listed in the schema below.
           Always qualify table names with the schema name (e.g. prodavnica.kupci).
        4. Output a single statement. Do not end with a semicolon.

        NOTES ON THIS DATABASE:
        - Identifiers are Serbian written without diacritics: "kolicina", "porudzbine",
          "menadzer_id", "broj_indeksa". Column values and comments DO use diacritics
          (for example status = 'isporučena', segment = 'maloprodaja').
        - The question may be written in Serbian or in English. Answer both the same way.
        - When the question asks for names of people, return the name columns
          (ime, prezime), not only the id.
        - For "top N" style questions use ORDER BY together with LIMIT.
        - For text matching prefer ILIKE over LIKE, because the data is in Serbian
          and casing varies.
        """;

    public static string OpisSeme(DatabaseSchema sema, IReadOnlyCollection<string>? izabraneTabele = null)
    {
        var vidljive = OdrediVidljiveTabele(sema, izabraneTabele);
        var sb = new StringBuilder();

        sb.Append("-- Schema: ").Append(sema.Naziv);
        if (!string.IsNullOrWhiteSpace(sema.Opis))
            sb.Append("  (").Append(sema.Opis).Append(')');
        sb.AppendLine();
        sb.AppendLine();

        foreach (var t in vidljive)
        {
            if (!string.IsNullOrWhiteSpace(t.Opis))
                sb.Append("-- ").AppendLine(t.Opis);

            sb.Append("CREATE TABLE ").Append(t.PunoIme).AppendLine(" (");

            for (var i = 0; i < t.Kolone.Count; i++)
            {
                var k = t.Kolone[i];
                sb.Append("    ").Append(k.Naziv).Append(' ').Append(k.Tip);
                if (k.PrimarniKljuc) sb.Append(" PRIMARY KEY");
                if (!k.MozeBitiNull && !k.PrimarniKljuc) sb.Append(" NOT NULL");
                if (i < t.Kolone.Count - 1) sb.Append(',');

                if (!string.IsNullOrWhiteSpace(k.Opis))
                    sb.Append("  -- ").Append(k.Opis);

                sb.AppendLine();
            }

            sb.AppendLine(");");

            foreach (var fk in t.StraniKljucevi)
                sb.Append("-- FK: ").Append(t.PunoIme).Append('.').Append(fk.Kolona)
                  .Append(" -> ").Append(sema.Naziv).Append('.').Append(fk.CiljnaTabela)
                  .Append('.').AppendLine(fk.CiljnaKolona);

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    public static string KorisnickiPrompt(
        DatabaseSchema sema,
        string pitanje,
        IReadOnlyCollection<string>? izabraneTabele = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(OpisSeme(sema, izabraneTabele));
        sb.AppendLine();
        sb.AppendLine("Question:");
        sb.AppendLine(pitanje.Trim());
        sb.AppendLine();
        sb.Append("SQL:");
        return sb.ToString();
    }

    private static List<TableInfo> OdrediVidljiveTabele(
        DatabaseSchema sema, IReadOnlyCollection<string>? izabrane)
    {
        if (izabrane is null || izabrane.Count == 0)
            return sema.Tabele;

        var skup = new HashSet<string>(izabrane, StringComparer.OrdinalIgnoreCase);

        foreach (var t in sema.Tabele.Where(t => skup.Contains(t.Naziv)).ToList())
            foreach (var fk in t.StraniKljucevi)
                skup.Add(fk.CiljnaTabela);

        foreach (var t in sema.Tabele)
            if (t.StraniKljucevi.Any(fk => skup.Contains(fk.CiljnaTabela)) && skup.Contains(t.Naziv))
                skup.Add(t.Naziv);

        return sema.Tabele.Where(t => skup.Contains(t.Naziv)).ToList();
    }
}
