using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Evaluation;
using SqlQueryEvaluator.Core.Llm;
using SqlQueryEvaluator.Core.Persistence;
using SqlQueryEvaluator.Core.TextToSql;

namespace SqlQueryEvaluator.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registruje sve što i API i benchmark runner koriste — dve veze ka bazi,
    /// registar modela, text-to-SQL sloj i evaluaciju.
    /// </summary>
    public static IServiceCollection DodajSqlQueryEvaluator(
        this IServiceCollection servisi, IConfiguration konfiguracija)
    {
        // Web host sam registruje IConfiguration; konzolni runner ne, pa se
        // dodaje ovde. TryAdd znači da se u API-ju ništa ne prepisuje.
        servisi.TryAddSingleton(konfiguracija);

        servisi.Configure<TextToSqlOptions>(konfiguracija.GetSection(TextToSqlOptions.SectionName));
        servisi.Configure<List<ModelDescriptor>>(konfiguracija.GetSection("Models"));

        servisi.AddMemoryCache();

        // Dve odvojene veze: jedna sa punim pravima za radne podatke aplikacije,
        // druga pod rolom sqleval_citanje isključivo za generisani SQL.
        servisi.AddSingleton(_ =>
            new AplikacijaDataSource(NpgsqlDataSource.Create(
                konfiguracija.GetConnectionString("AplikacijaDb")
                ?? throw new InvalidOperationException("Nedostaje ConnectionStrings:AplikacijaDb"))));

        servisi.AddSingleton(_ =>
            new UpitDataSource(NpgsqlDataSource.Create(
                konfiguracija.GetConnectionString("UpitDb")
                ?? throw new InvalidOperationException("Nedostaje ConnectionStrings:UpitDb"))));

        // Besplatni tierovi imaju stroge rate limite — bez ponavljanja uz
        // eksponencijalni backoff benchmark od 360+ poziva pukne na pola.
        servisi.AddHttpClient("llm", klijent =>
        {
            klijent.Timeout = TimeSpan.FromSeconds(120);
        })
        .AddStandardResilienceHandler(opcije =>
        {
            // 429 se NAMERNO ne ponavlja ovde. Polly poštuje zaglavlje
            // Retry-After, a Groq u njemu traži i po 337 sekundi — to probije
            // ukupni timeout ispod i ceo poziv padne kao "timeout", pa se ne
            // vidi da je u pitanju kvota. Zato 429 ide netaknut do runner-a,
            // koji sačeka koliko treba i ponovi zadatak.
            opcije.Retry.ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Exception is HttpRequestException
                || (args.Outcome.Result is { } o && (int)o.StatusCode >= 500));

            opcije.Retry.MaxRetryAttempts = 2;
            opcije.Retry.Delay = TimeSpan.FromSeconds(2);
            opcije.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            opcije.Retry.UseJitter = true;
            opcije.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
            opcije.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(180);
            opcije.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
        });

        servisi.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
        servisi.AddSingleton<SchemaIntrospector>();
        servisi.AddSingleton<QueryExecutor>();
        servisi.AddSingleton<LlmJudge>();
        servisi.AddSingleton<TextToSqlService>();
        servisi.AddSingleton<ExecutionAccuracyEvaluator>();
        servisi.AddSingleton<IstorijaRepository>();
        servisi.AddSingleton<BenchmarkRepository>();

        return servisi;
    }

    /// <summary>Minimalno logovanje za konzolni runner (API ga već ima iz host-a).</summary>
    public static IServiceCollection AddSqlQueryEvaluatorLogging(this IServiceCollection servisi)
    {
        servisi.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true)
                                 .SetMinimumLevel(LogLevel.Warning));
        return servisi;
    }
}
