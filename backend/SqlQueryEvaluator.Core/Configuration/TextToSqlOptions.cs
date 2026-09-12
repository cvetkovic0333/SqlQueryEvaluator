namespace SqlQueryEvaluator.Core.Configuration;

public sealed class TextToSqlOptions
{
    public const string SectionName = "TextToSql";

    public string DefaultModelId { get; set; } = "";

    public string JudgeModelId { get; set; } = "";

    public List<string> Baze { get; set; } = [];

    public int MaxRows { get; set; } = 500;

    public int StatementTimeoutSeconds { get; set; } = 5;

    public int MinimalnaOcenaZaIzvrsavanje { get; set; } = 4;
}
