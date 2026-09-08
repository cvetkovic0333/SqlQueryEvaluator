namespace SqlQueryEvaluator.Core.Configuration;

/// <summary>
/// Opis jednog modela iz registra u appsettings.json.
/// Novi model se dodaje jednim JSON unosom — bez pisanja koda.
/// </summary>
public sealed class ModelDescriptor
{
    /// <summary>Jedinstven identifikator, npr. "groq:llama-3.3-70b".</summary>
    public string Id { get; set; } = "";

    /// <summary>Prikazno ime u korisničkom interfejsu.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Naziv provajdera za grupisanje u grafikonima (Groq, Gemini, ...).</summary>
    public string Provider { get; set; } = "";

    /// <summary>"openai" za sve OpenAI-kompatibilne API-je, "gemini" za Google.</summary>
    public string Kind { get; set; } = "openai";

    /// <summary>Osnovni URL API-ja; za Gemini se ne koristi.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Naziv modela koji API očekuje u telu zahteva.</summary>
    public string Model { get; set; } = "";

    /// <summary>
    /// Ključ pod kojim se traži API ključ: prvo u konfiguraciji kao
    /// "ApiKeys:{ApiKeyRef}", pa u promenljivoj okruženja "{APIKEYREF}_API_KEY".
    /// </summary>
    public string ApiKeyRef { get; set; } = "";

    public double Temperature { get; set; }
    public int MaxTokens { get; set; } = 1200;
    public bool Enabled { get; set; } = true;

    public string PrikaznoIme => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}
