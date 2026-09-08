namespace SqlQueryEvaluator.Core.Models;

/// <summary>Jezik na kome je postavljeno pitanje.</summary>
public enum Jezik
{
    Sr,
    En
}

/// <summary>Težina test zadatka.</summary>
public enum Tezina
{
    Lak,
    Srednji,
    Tezak
}

/// <summary>Rezultat izvršavanja SQL upita nad bazom.</summary>
public sealed class QueryResult
{
    public List<string> Kolone { get; init; } = [];
    public List<List<object?>> Redovi { get; init; } = [];
    public int BrojRedova => Redovi.Count;
    public long TrajanjeMs { get; set; }
    public bool Odsecen { get; set; }

    public bool Uspesno { get; init; } = true;
    public string? Greska { get; init; }
    public string? SqlState { get; init; }

    public static QueryResult Neuspesno(string greska, string? sqlState = null) =>
        new() { Uspesno = false, Greska = greska, SqlState = sqlState };
}

/// <summary>Ishod provere SQL-a pre izvršavanja.</summary>
public sealed record SanitizerResult(bool Prihvacen, string Sql, string? Razlog)
{
    public static SanitizerResult Ok(string sql) => new(true, sql, null);
    public static SanitizerResult Odbijen(string razlog) => new(false, "", razlog);
}
