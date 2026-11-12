using Npgsql;
using Store.Web.Models;

namespace Store.Web.Data;

/// <summary>
/// One scoped connection and one transaction shared by every repository.
/// The transaction is owned here, never by an individual repository.
/// </summary>
public sealed class PostgreSqlUnitOfWork : IUnitOfWork
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private bool _disposed;

    public PostgreSqlUnitOfWork(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        Products = new ProductRepository(this);
        Inventory = new InventoryRepository(this);
        Sales = new SaleRepository(this);
        Payments = new PaymentRepository(this);
        Supplies = new SupplyRepository(this);
        Lookups = new LookupRepository(this);
    }

    public IProductRepository Products { get; }
    public IInventoryRepository Inventory { get; }
    public ISaleRepository Sales { get; }
    public IPaymentRepository Payments { get; }
    public ISupplyRepository Supplies { get; }
    public ILookupRepository Lookups { get; }

    private async Task<NpgsqlConnection> ConnectionAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _connection ??= await _dataSource.OpenConnectionAsync(ct);
    }

    public async Task BeginAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var connection = await ConnectionAsync(ct);
            if (_transaction is not null)
                throw new InvalidOperationException("A transaction is already active.");
            _transaction = await connection.BeginTransactionAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var transaction = _transaction
                ?? throw new InvalidOperationException("There is no transaction to commit.");
            // If commit fails, retain the transaction so the caller can roll it back.
            await transaction.CommitAsync(ct);
            _transaction = null;
            await transaction.DisposeAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_transaction is null) return;
            var transaction = _transaction;
            try { await transaction.RollbackAsync(ct); }
            finally
            {
                _transaction = null;
                await transaction.DisposeAsync();
            }
        }
        finally { _gate.Release(); }
    }

    internal async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql, Func<NpgsqlDataReader, T> map, CancellationToken ct,
        params NpgsqlParameter[] parameters)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var connection = await ConnectionAsync(ct);
            await using var command = new NpgsqlCommand(sql, connection, _transaction);
            command.Parameters.AddRange(parameters);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var rows = new List<T>();
            while (await reader.ReadAsync(ct)) rows.Add(map(reader));
            return rows;
        }
        finally { _gate.Release(); }
    }

    internal async Task<long> CreateAsync(
        string sql, CancellationToken ct, params NpgsqlParameter[] parameters)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var connection = await ConnectionAsync(ct);
            RequireTransaction();
            await using var command = new NpgsqlCommand(sql, connection, _transaction);
            command.Parameters.AddRange(parameters);
            var result = await command.ExecuteScalarAsync(ct);
            return result is long id
                ? id
                : throw new InvalidOperationException("The procedure did not return a record identifier.");
        }
        finally { _gate.Release(); }
    }

    internal async Task ExecuteAsync(
        string sql, CancellationToken ct, params NpgsqlParameter[] parameters)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var connection = await ConnectionAsync(ct);
            RequireTransaction();
            await using var command = new NpgsqlCommand(sql, connection, _transaction);
            command.Parameters.AddRange(parameters);
            await command.ExecuteNonQueryAsync(ct);
        }
        finally { _gate.Release(); }
    }

    private void RequireTransaction()
    {
        if (_transaction is null)
            throw new InvalidOperationException("BeginAsync must be called before modifying data.");
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                // Npgsql rolls back an unfinished transaction on disposal.
                if (_transaction is not null) await _transaction.DisposeAsync();
            }
            finally
            {
                _transaction = null;
                if (_connection is not null) await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally { _gate.Release(); }
    }
}

