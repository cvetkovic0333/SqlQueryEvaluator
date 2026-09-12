using Npgsql;

namespace SqlQueryEvaluator.Core.Persistence;

public sealed class AplikacijaDataSource(NpgsqlDataSource izvor) : IDisposable
{
    public NpgsqlDataSource Izvor { get; } = izvor;
    public void Dispose() => Izvor.Dispose();
}

public sealed class UpitDataSource(NpgsqlDataSource izvor) : IDisposable
{
    public NpgsqlDataSource Izvor { get; } = izvor;
    public void Dispose() => Izvor.Dispose();
}
