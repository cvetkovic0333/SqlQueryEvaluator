using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlQueryEvaluator.Benchmark.Runner;
using SqlQueryEvaluator.Core;

// Runner se pokreće iz korena projekta (zbog putanja benchmark/testset.json
// i benchmark/results/), pa se radni direktorijum pomera ako je pokrenut iz
// bin foldera preko `dotnet run`.
PodesiRadniDirektorijum();

var konfiguracija = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var servisi = new ServiceCollection();
servisi.AddSqlQueryEvaluatorLogging();
servisi.DodajSqlQueryEvaluator(konfiguracija);
servisi.AddSingleton<BenchmarkRunner>();

using var provajder = servisi.BuildServiceProvider();
var runner = provajder.GetRequiredService<BenchmarkRunner>();

using var prekid = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    prekid.Cancel();
    Console.WriteLine("\nPrekidam… (rezultati do sada su već upisani; nastavi sa --resume)");
};

try
{
    return await runner.PokreniAsync(Argumenti.Rasclani(args), prekid.Token);
}
catch (OperationCanceledException)
{
    return 130;
}

static void PodesiRadniDirektorijum()
{
    var direktorijum = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (direktorijum is not null && !File.Exists(Path.Combine(direktorijum.FullName, "SqlQueryEvaluator.sln")))
        direktorijum = direktorijum.Parent;

    if (direktorijum is not null)
        Directory.SetCurrentDirectory(direktorijum.FullName);
}

public partial class Program;
