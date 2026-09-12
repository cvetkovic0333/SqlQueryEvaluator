namespace SqlQueryEvaluator.Core.Models;

public enum Jezik
{
    Sr,
    En
}

public enum Tezina
{
    Lak,
    Srednji,
    Tezak
}

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

public sealed record SanitizerResult(bool Prihvacen, string Sql, string? Razlog)
{
    public static SanitizerResult Ok(string sql) => new(true, sql, null);
    public static SanitizerResult Odbijen(string razlog) => new(false, "", razlog);
}
