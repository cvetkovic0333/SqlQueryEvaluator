namespace SqlQueryEvaluator.Core.Configuration;

public sealed class ModelDescriptor
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Provider { get; set; } = "";

    public string Kind { get; set; } = "openai";

    public string BaseUrl { get; set; } = "";

    public string Model { get; set; } = "";

    public string ApiKeyRef { get; set; } = "";

    public double Temperature { get; set; }
    public int MaxTokens { get; set; } = 1200;
    public bool Enabled { get; set; } = true;

    public string PrikaznoIme => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}
