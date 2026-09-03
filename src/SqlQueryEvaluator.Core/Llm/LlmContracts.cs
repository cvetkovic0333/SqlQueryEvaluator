using SqlQueryEvaluator.Core.Configuration;

namespace SqlQueryEvaluator.Core.Llm;

public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    double Temperature = 0,
    int MaxTokens = 1200,
    bool JsonMode = false);

public sealed record LlmResponse(
    string Text,
    int UlazniTokeni,
    int IzlazniTokeni,
    long TrajanjeMs,
    string ModelId);

/// <summary>
/// Greška u komunikaciji sa modelom. <see cref="RateLimit"/> se posebno
/// izdvaja jer besplatni tierovi često vraćaju 429 i benchmark na to reaguje
/// pauzom umesto prekidom.
/// </summary>
public sealed class LlmException(string poruka, int? statusKod = null, Exception? uzrok = null)
    : Exception(poruka, uzrok)
{
    public int? StatusKod { get; } = statusKod;
    public bool RateLimit => StatusKod == 429;
}

public interface ILlmProvider
{
    ModelDescriptor Opis { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest zahtev, CancellationToken ct = default);
}

public interface ILlmProviderFactory
{
    IReadOnlyList<ModelDescriptor> DostupniModeli { get; }
    bool ImaKljuc(string modelId);
    ILlmProvider Kreiraj(string modelId);
}
