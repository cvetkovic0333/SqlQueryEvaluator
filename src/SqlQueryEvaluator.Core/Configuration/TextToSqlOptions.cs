namespace SqlQueryEvaluator.Core.Configuration;

public sealed class TextToSqlOptions
{
    public const string SectionName = "TextToSql";

    /// <summary>Model koji aplikacija koristi za generisanje SQL-a (pobednik testa).</summary>
    public string DefaultModelId { get; set; } = "";

    /// <summary>Model u ulozi sudije. Namerno različit od generatora — self-preference bias.</summary>
    public string JudgeModelId { get; set; } = "";

    /// <summary>Šeme koje se nude u aplikaciji kao "baze" za filtriranje.</summary>
    public List<string> Baze { get; set; } = [];

    /// <summary>Gornja granica broja redova koji se vraćaju korisničkom interfejsu.</summary>
    public int MaxRows { get; set; } = 500;

    /// <summary>Koliko sekundi upit sme da traje pre nego što ga baza prekine.</summary>
    public int StatementTimeoutSeconds { get; set; } = 5;

    /// <summary>Ispod ove ocene sudije aplikacija traži izričitu potvrdu pre izvršavanja.</summary>
    public int MinimalnaOcenaZaIzvrsavanje { get; set; } = 4;
}
