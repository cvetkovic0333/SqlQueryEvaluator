using System.Data;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using Npgsql;
using SqlQueryEvaluator.Core.Configuration;
using SqlQueryEvaluator.Core.Models;
using SqlQueryEvaluator.Core.Persistence;

namespace SqlQueryEvaluator.Core.TextToSql;

public sealed class QueryExecutor(UpitDataSource izvor, IOptions<TextToSqlOptions> opcije)
{
    private readonly TextToSqlOptions _opcije = opcije.Value;

    public Task<QueryResult> IzvrsiAsync(string sql, CancellationToken ct = default)
        => IzvrsiAsync(sql, _opcije.MaxRows, ct);

    public async Task<QueryResult> IzvrsiAsync(string sql, int maxRedova, CancellationToken ct = default)
    {
        var sat = Stopwatch.StartNew();

        try
        {
            await using var veza = await izvor.Izvor.OpenConnectionAsync(ct);
            await using var transakcija = await veza.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            await using (var podesavanje = new NpgsqlCommand(
                $"SET LOCAL statement_timeout = '{_opcije.StatementTimeoutSeconds}s'; " +
                "SET LOCAL transaction_read_only = on;", veza, transakcija))
            {
                await podesavanje.ExecuteNonQueryAsync(ct);
            }

            var omotan = $"SELECT * FROM (\n{sql}\n) AS _rezultat LIMIT {maxRedova + 1}";

            var kaoTekst = await NumericKoloneAsync(omotan, veza, transakcija, ct);

            await using var komanda = new NpgsqlCommand(omotan, veza, transakcija);
            if (kaoTekst.Any(k => k))
                komanda.UnknownResultTypeList = kaoTekst;

            await using var citac = await komanda.ExecuteReaderAsync(ct);

            var rezultat = new QueryResult();
            for (var i = 0; i < citac.FieldCount; i++)
                rezultat.Kolone.Add(citac.GetName(i));

            while (await citac.ReadAsync(ct))
            {
                if (rezultat.Redovi.Count >= maxRedova)
                {
                    rezultat.Odsecen = true;
                    break;
                }

                var red = new List<object?>(citac.FieldCount);
                for (var i = 0; i < citac.FieldCount; i++)
                    red.Add(citac.IsDBNull(i) ? null
                        : kaoTekst[i] ? ProcitajNumeric(citac.GetString(i))
                        : Normalizuj(citac.GetValue(i)));

                rezultat.Redovi.Add(red);
            }

            await citac.CloseAsync();
            await transakcija.RollbackAsync(ct);

            sat.Stop();
            rezultat.TrajanjeMs = sat.ElapsedMilliseconds;
            return rezultat;
        }
        catch (PostgresException ex)
        {
            return QueryResult.Neuspesno(PrevediGresku(ex), ex.SqlState);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return QueryResult.Neuspesno(ex.Message);
        }
    }

    private static async Task<bool[]> NumericKoloneAsync(
        string sql, NpgsqlConnection veza, NpgsqlTransaction transakcija, CancellationToken ct)
    {
        await using var opis = new NpgsqlCommand(sql, veza, transakcija);
        await using var citac = await opis.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct);

        var rezultat = new bool[citac.FieldCount];
        for (var i = 0; i < citac.FieldCount; i++)
            rezultat[i] = citac.GetDataTypeName(i).StartsWith("numeric", StringComparison.Ordinal);
        return rezultat;
    }

    private static object ProcitajNumeric(string tekst) =>
        decimal.TryParse(tekst, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d
        : double.TryParse(tekst, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.IsFinite(x) ? x
        : tekst;

    private static object? Normalizuj(object vrednost) => vrednost switch
    {
        DateTime d => d.TimeOfDay == TimeSpan.Zero
            ? d.ToString("yyyy-MM-dd")
            : d.ToString("yyyy-MM-dd HH:mm:ss"),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:sszzz"),
        DateOnly dat => dat.ToString("yyyy-MM-dd"),
        TimeSpan t => t.ToString(),
        byte[] b => Convert.ToBase64String(b),
        _ => vrednost
    };

    private static string PrevediGresku(PostgresException ex) => ex.SqlState switch
    {
        "42P01" => $"Tabela ne postoji: {ex.MessageText}",
        "42703" => $"Kolona ne postoji: {ex.MessageText}",
        "42601" => $"Sintaksna greška u upitu: {ex.MessageText}",
        "42883" => $"Funkcija ne postoji: {ex.MessageText}",
        "42501" => "Nemate pravo pristupa nad tim objektom.",
        "57014" => "Upit je prekinut jer je trajao duže od dozvoljenog.",
        "25006" => "Pokušana je izmena podataka u read-only transakciji.",
        _ => ex.MessageText
    };
}
