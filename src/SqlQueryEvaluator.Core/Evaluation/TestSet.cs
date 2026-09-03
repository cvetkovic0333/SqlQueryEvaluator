using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlQueryEvaluator.Core.Evaluation;

public sealed class TestZadatak
{
    public string Id { get; set; } = "";

    /// <summary>"lak", "srednji" ili "tezak".</summary>
    public string Tezina { get; set; } = "lak";

    /// <summary>Šema nad kojom se zadatak rešava: "prodavnica" ili "fakultet".</summary>
    public string Baza { get; set; } = "prodavnica";

    /// <summary>Pitanje na srpskom.</summary>
    public string PitanjeSr { get; set; } = "";

    /// <summary>Isto pitanje na engleskom — omogućava poređenje po jeziku.</summary>
    public string PitanjeEn { get; set; } = "";

    /// <summary>Tačan upit, ručno napisan i proveren nad bazom.</summary>
    public string GoldSql { get; set; } = "";

    /// <summary>Oznake SQL konstrukcija koje zadatak testira (join, window, cte...).</summary>
    public List<string> Oznake { get; set; } = [];

    public string Pitanje(string jezik) =>
        jezik.Equals("en", StringComparison.OrdinalIgnoreCase) ? PitanjeEn : PitanjeSr;
}

public sealed class TestSet
{
    public int Verzija { get; set; } = 1;
    public string Opis { get; set; } = "";
    public List<TestZadatak> Zadaci { get; set; } = [];

    private static readonly JsonSerializerOptions Opcije = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task<TestSet> UcitajAsync(string putanja, CancellationToken ct = default)
    {
        if (!File.Exists(putanja))
            throw new FileNotFoundException($"Test set nije pronađen: {putanja}");

        await using var tok = File.OpenRead(putanja);
        var set = await JsonSerializer.DeserializeAsync<TestSet>(tok, Opcije, ct)
                  ?? throw new InvalidDataException($"Test set {putanja} nije ispravan JSON.");

        var bezGolda = set.Zadaci.Where(z => string.IsNullOrWhiteSpace(z.GoldSql)).Select(z => z.Id).ToList();
        if (bezGolda.Count > 0)
            throw new InvalidDataException($"Zadaci bez gold SQL-a: {string.Join(", ", bezGolda)}");

        var duplikati = set.Zadaci.GroupBy(z => z.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplikati.Count > 0)
            throw new InvalidDataException($"Duplirani id zadataka: {string.Join(", ", duplikati)}");

        return set;
    }
}
