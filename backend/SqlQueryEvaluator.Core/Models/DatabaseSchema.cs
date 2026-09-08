namespace SqlQueryEvaluator.Core.Models;

/// <summary>
/// Opis jedne šeme pročitan iz information_schema. Isti model koristi i
/// korisnički interfejs (prikaz "učitane baze") i graditelj prompta —
/// jedan izvor istine, pa UI i model uvek vide isto.
/// </summary>
public sealed class DatabaseSchema
{
    public required string Naziv { get; init; }
    public string? Opis { get; init; }
    public List<TableInfo> Tabele { get; init; } = [];
}

public sealed class TableInfo
{
    public required string Sema { get; init; }
    public required string Naziv { get; init; }
    public string? Opis { get; init; }
    public long BrojRedova { get; set; }
    public List<ColumnInfo> Kolone { get; init; } = [];
    public List<ForeignKeyInfo> StraniKljucevi { get; init; } = [];

    public string PunoIme => $"{Sema}.{Naziv}";
}

public sealed class ColumnInfo
{
    public required string Naziv { get; init; }
    public required string Tip { get; init; }
    public bool MozeBitiNull { get; init; }
    public bool PrimarniKljuc { get; init; }
    public string? Opis { get; init; }
}

public sealed class ForeignKeyInfo
{
    public required string Kolona { get; init; }
    public required string CiljnaTabela { get; init; }
    public required string CiljnaKolona { get; init; }
}
