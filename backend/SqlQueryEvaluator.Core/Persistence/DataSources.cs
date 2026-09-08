using Npgsql;

namespace SqlQueryEvaluator.Core.Persistence;

/// <summary>
/// Veza kojom aplikacija piše svoje radne podatke (istorija, benchmark).
/// Puna prava nad šemom "aplikacija".
/// </summary>
public sealed class AplikacijaDataSource(NpgsqlDataSource izvor) : IDisposable
{
    public NpgsqlDataSource Izvor { get; } = izvor;
    public void Dispose() => Izvor.Dispose();
}

/// <summary>
/// Veza pod rolom sqleval_citanje — jedino preko nje se izvršava SQL koji je
/// generisao model. Dva odvojena tipa umesto jednog postoje namerno: nemoguće
/// je greškom proslediti vezu sa punim pravima tamo gde se izvršava
/// generisani SQL, jer se tipovi ne poklapaju.
/// </summary>
public sealed class UpitDataSource(NpgsqlDataSource izvor) : IDisposable
{
    public NpgsqlDataSource Izvor { get; } = izvor;
    public void Dispose() => Izvor.Dispose();
}
