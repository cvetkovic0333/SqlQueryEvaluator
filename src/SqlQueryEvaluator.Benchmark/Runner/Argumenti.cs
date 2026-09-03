namespace SqlQueryEvaluator.Benchmark.Runner;

/// <summary>Argumenti komandne linije za benchmark.</summary>
public sealed class Argumenti
{
    public List<string> Modeli { get; private set; } = [];
    public List<string> Tezine { get; private set; } = [];
    public List<string> Jezici { get; private set; } = ["sr", "en"];
    public string? Baza { get; private set; }
    public int? Limit { get; private set; }
    public int? NastaviPokretanje { get; private set; }
    public bool SamoProvera { get; private set; }
    public int PauzaMs { get; private set; } = 1200;
    public string? ModelSudije { get; private set; }
    public string TestSetPutanja { get; private set; } = "benchmark/testset.json";
    public bool Pomoc { get; private set; }

    public static Argumenti Rasclani(string[] args)
    {
        var a = new Argumenti();

        for (var i = 0; i < args.Length; i++)
        {
            var kljuc = args[i];
            string? Sledeci() => i + 1 < args.Length ? args[++i] : null;

            switch (kljuc)
            {
                case "--models" or "--modeli":
                    a.Modeli = (Sledeci() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries
                                                          | StringSplitOptions.TrimEntries).ToList();
                    break;
                case "--difficulty" or "--tezina":
                    a.Tezine = (Sledeci() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries
                                                          | StringSplitOptions.TrimEntries).ToList();
                    break;
                case "--languages" or "--jezici":
                    a.Jezici = (Sledeci() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries
                                                          | StringSplitOptions.TrimEntries).ToList();
                    break;
                case "--baza":
                    a.Baza = Sledeci();
                    break;
                case "--limit":
                    a.Limit = int.TryParse(Sledeci(), out var l) ? l : null;
                    break;
                case "--resume" or "--nastavi":
                    a.NastaviPokretanje = int.TryParse(Sledeci(), out var r) ? r : null;
                    break;
                case "--dry-run" or "--provera":
                    a.SamoProvera = true;
                    break;
                case "--delay" or "--pauza":
                    a.PauzaMs = int.TryParse(Sledeci(), out var p) ? p : 1200;
                    break;
                case "--judge" or "--sudija":
                    a.ModelSudije = Sledeci();
                    break;
                case "--testset":
                    a.TestSetPutanja = Sledeci() ?? a.TestSetPutanja;
                    break;
                case "--help" or "-h":
                    a.Pomoc = true;
                    break;
            }
        }

        return a;
    }

    public const string TekstPomoci = """
        SqlQueryEvaluator.Benchmark — testira modele nad test setom i upisuje rezultate.

          --models    <lista>   samo navedeni modeli, npr. groq:llama-3.3-70b,gemini:2.5-flash
          --difficulty <lista>  samo navedene težine: lak,srednji,tezak
          --languages <lista>   jezici pitanja (podrazumevano sr,en)
          --baza      <naziv>   samo zadaci nad tom šemom: prodavnica ili fakultet
          --limit     <broj>    najviše toliko zadataka (za brzu probu)
          --resume    <id>      nastavi prekinuto pokretanje, preskačući već urađeno
          --delay     <ms>      pauza između poziva (podrazumevano 1200)
          --judge     <model>   model u ulozi sudije (podrazumevano iz appsettings.json)
          --dry-run             samo proveri koji modeli imaju ispravan API ključ
          --help                ovaj tekst

        Primeri:
          dotnet run --project src/SqlQueryEvaluator.Benchmark -- --dry-run
          dotnet run --project src/SqlQueryEvaluator.Benchmark -- --limit 5
          dotnet run --project src/SqlQueryEvaluator.Benchmark -- --resume 3
        """;
}
