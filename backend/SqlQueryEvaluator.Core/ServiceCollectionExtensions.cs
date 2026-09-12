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
    public static IServiceCollection DodajSqlQueryEvaluator(
        this IServiceCollection servisi, IConfiguration konfiguracija)
    {
        servisi.TryAddSingleton(konfiguracija);

        servisi.Configure<TextToSqlOptions>(konfiguracija.GetSection(TextToSqlOptions.SectionName));
        servisi.Configure<List<ModelDescriptor>>(konfiguracija.GetSection("Models"));

        servisi.AddMemoryCache();

        servisi.AddSingleton(_ =>
            new AplikacijaDataSource(NpgsqlDataSource.Create(
                konfiguracija.GetConnectionString("AplikacijaDb")
                ?? throw new InvalidOperationException("Nedostaje ConnectionStrings:AplikacijaDb"))));

        servisi.AddSingleton(_ =>
            new UpitDataSource(NpgsqlDataSource.Create(
                konfiguracija.GetConnectionString("UpitDb")
                ?? throw new InvalidOperationException("Nedostaje ConnectionStrings:UpitDb"))));

        servisi.AddHttpClient("llm", klijent =>
        {
            klijent.Timeout = TimeSpan.FromSeconds(120);
        })
        .AddStandardResilienceHandler(opcije =>
        {
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
        servisi.AddSingleton<NajboljiModel>();
        servisi.AddSingleton<IstorijaRepository>();
        servisi.AddSingleton<BenchmarkRepository>();

        return servisi;
    }

    public static IServiceCollection AddSqlQueryEvaluatorLogging(this IServiceCollection servisi)
    {
        servisi.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true)
                                 .SetMinimumLevel(LogLevel.Warning));
        return servisi;
    }
}
