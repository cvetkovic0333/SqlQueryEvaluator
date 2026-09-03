using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Llm.Providers;

namespace SqlQueryEvaluator.Core.Llm;

/// <summary>
/// Registar modela. Čita listu iz appsettings.json i za svaki traženi model
/// pravi odgovarajućeg provajdera.
///
/// API ključ se traži na dva mesta, tim redom:
///   1. konfiguracija: "ApiKeys:{ApiKeyRef}"  (dotnet user-secrets)
///   2. promenljiva okruženja: "{APIKEYREF}_API_KEY"
/// Ključevi nikada ne stoje u appsettings.json — repo je javan.
/// </summary>
public sealed class LlmProviderFactory(
    IOptions<List<ModelDescriptor>> modeli,
    IConfiguration konfiguracija,
    IHttpClientFactory httpFactory) : ILlmProviderFactory
{
    private readonly List<ModelDescriptor> _modeli = modeli.Value.Where(m => m.Enabled).ToList();

    public IReadOnlyList<ModelDescriptor> DostupniModeli => _modeli;

    public bool ImaKljuc(string modelId)
    {
        var opis = _modeli.FirstOrDefault(m => m.Id == modelId);
        return opis is not null && !string.IsNullOrWhiteSpace(NadjiKljuc(opis.ApiKeyRef));
    }

    public ILlmProvider Kreiraj(string modelId)
    {
        var opis = _modeli.FirstOrDefault(m => m.Id == modelId)
            ?? throw new LlmException($"Model '{modelId}' nije u registru (appsettings.json → Models).");

        var kljuc = NadjiKljuc(opis.ApiKeyRef);
        if (string.IsNullOrWhiteSpace(kljuc))
            throw new LlmException(
                $"Nedostaje API ključ za '{opis.Id}'. Postavi ga sa: " +
                $"dotnet user-secrets set \"ApiKeys:{opis.ApiKeyRef}\" \"...\" " +
                $"ili preko promenljive okruženja {opis.ApiKeyRef.ToUpperInvariant()}_API_KEY");

        var http = httpFactory.CreateClient("llm");

        return opis.Kind.ToLowerInvariant() switch
        {
            "gemini" => new GeminiProvider(opis, http, kljuc),
            "openai" => new OpenAiCompatibleProvider(opis, http, kljuc),
            _ => throw new LlmException($"Nepoznat tip provajdera '{opis.Kind}' za model '{opis.Id}'.")
        };
    }

    private string? NadjiKljuc(string apiKeyRef)
    {
        if (string.IsNullOrWhiteSpace(apiKeyRef))
            return null;

        var izKonfiguracije = konfiguracija[$"ApiKeys:{apiKeyRef}"];
        if (!string.IsNullOrWhiteSpace(izKonfiguracije))
            return izKonfiguracije;

        return Environment.GetEnvironmentVariable($"{apiKeyRef.ToUpperInvariant()}_API_KEY");
    }
}
