using System.Globalization;
using Microsoft.Data.Sqlite;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Sales;

namespace Playloud.Persistence.Sqlite;

public sealed record SqliteHealth(bool ForeignKeysEnabled, string JournalMode);

public sealed record StoredProduct(
    EntityId<Product> Id,
    string Name,
    decimal UnitPrice,
    decimal UnitCost);

public sealed class SqliteCommerceStore : IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteCommerceStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using (var wal = connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode=WAL;";
            _ = await wal.ExecuteScalarAsync(cancellationToken);
        }

        await using var schema = connection.CreateCommand();
        schema.CommandText = """
            CREATE TABLE IF NOT EXISTS products (
                id TEXT PRIMARY KEY NOT NULL,
                name TEXT NOT NULL,
                unit_price TEXT NOT NULL,
                unit_cost TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS stock (
                product_id TEXT PRIMARY KEY NOT NULL,
                quantity TEXT NOT NULL,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS sales (
                id TEXT PRIMARY KEY NOT NULL,
                business_date TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sale_lines (
                id TEXT PRIMARY KEY NOT NULL,
                sale_id TEXT NOT NULL,
                product_id TEXT NOT NULL,
                product_name TEXT NOT NULL,
                quantity TEXT NOT NULL,
                unit_price TEXT NOT NULL,
                unit_cost TEXT NOT NULL,
                FOREIGN KEY (sale_id) REFERENCES sales(id) ON DELETE CASCADE,
                FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT
            );

            PRAGMA user_version=1;
            """;
        await schema.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SqliteHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys;";
        var foreignKeysValue = Convert.ToInt32(
            await foreignKeys.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);

        await using var journal = connection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode;";
        var journalValue = Convert.ToString(
            await journal.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) ?? string.Empty;

        return new SqliteHealth(foreignKeysValue == 1, journalValue);
    }

    public async Task SaveProductAsync(
        Product product,
        decimal openingStock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (openingStock < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(openingStock), "Opening stock cannot be negative.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            await using (var productCommand = connection.CreateCommand())
            {
                productCommand.Transaction = transaction;
                productCommand.CommandText = """
                    INSERT INTO products (id, name, unit_price, unit_cost)
                    VALUES ($id, $name, $unitPrice, $unitCost);
                    """;
                productCommand.Parameters.AddWithValue("$id", product.Id.ToString());
                productCommand.Parameters.AddWithValue("$name", product.Name);
                productCommand.Parameters.AddWithValue("$unitPrice", ToStorageDecimal(product.UnitPrice));
                productCommand.Parameters.AddWithValue("$unitCost", ToStorageDecimal(product.UnitCost));
                await productCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var stockCommand = connection.CreateCommand())
            {
                stockCommand.Transaction = transaction;
                stockCommand.CommandText = """
                    INSERT INTO stock (product_id, quantity)
                    VALUES ($productId, $quantity);
                    """;
                stockCommand.Parameters.AddWithValue("$productId", product.Id.ToString());
                stockCommand.Parameters.AddWithValue("$quantity", ToStorageDecimal(openingStock));
                await stockCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<StoredProduct?> GetProductAsync(
        EntityId<Product> productId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, unit_price, unit_cost
            FROM products
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", productId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredProduct(
            new EntityId<Product>(Guid.Parse(reader.GetString(0))),
            reader.GetString(1),
            FromStorageDecimal(reader.GetString(2)),
            FromStorageDecimal(reader.GetString(3)));
    }

    public async Task<decimal> GetStockAsync(
        EntityId<Product> productId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT quantity FROM stock WHERE product_id = $productId;";
        command.Parameters.AddWithValue("$productId", productId.ToString());

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull)
        {
            throw new KeyNotFoundException($"Stock was not found for product {productId}.");
        }

        return FromStorageDecimal(Convert.ToString(value, CultureInfo.InvariantCulture)!);
    }

    public async Task CommitSaleAsync(Sale sale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sale);
        if (sale.Lines.Count == 0)
        {
            throw new InvalidOperationException("A sale must contain at least one line.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            await InsertSaleAsync(connection, transaction, sale, cancellationToken);

            foreach (var line in sale.Lines)
            {
                var currentStock = await ReadStockAsync(
                    connection,
                    transaction,
                    line.ProductId,
                    cancellationToken);

                if (currentStock < line.Quantity)
                {
                    throw new InvalidOperationException(
                        $"Insufficient current stock for product {line.ProductId}.");
                }

                await InsertSaleLineAsync(connection, transaction, sale.Id, line, cancellationToken);
                await WriteStockAsync(
                    connection,
                    transaction,
                    line.ProductId,
                    currentStock - line.Quantity,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<int> CountSalesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sales;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task<int> CountSaleLinesAsync(
        EntityId<Sale> saleId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sale_lines WHERE sale_id = $saleId;";
        command.Parameters.AddWithValue("$saleId", saleId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var configure = connection.CreateCommand();
        configure.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await configure.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }

    private static async Task InsertSaleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Sale sale,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sales (id, business_date)
            VALUES ($id, $businessDate);
            """;
        command.Parameters.AddWithValue("$id", sale.Id.ToString());
        command.Parameters.AddWithValue("$businessDate", sale.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSaleLineAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId<Sale> saleId,
        SaleLine line,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sale_lines (
                id, sale_id, product_id, product_name, quantity, unit_price, unit_cost)
            VALUES (
                $id, $saleId, $productId, $productName, $quantity, $unitPrice, $unitCost);
            """;
        command.Parameters.AddWithValue("$id", line.Id.ToString());
        command.Parameters.AddWithValue("$saleId", saleId.ToString());
        command.Parameters.AddWithValue("$productId", line.ProductId.ToString());
        command.Parameters.AddWithValue("$productName", line.ProductName);
        command.Parameters.AddWithValue("$quantity", ToStorageDecimal(line.Quantity));
        command.Parameters.AddWithValue("$unitPrice", ToStorageDecimal(line.UnitPrice));
        command.Parameters.AddWithValue("$unitCost", ToStorageDecimal(line.UnitCost));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<decimal> ReadStockAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId<Product> productId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT quantity FROM stock WHERE product_id = $productId;";
        command.Parameters.AddWithValue("$productId", productId.ToString());

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull)
        {
            throw new InvalidOperationException($"Stock was not found for product {productId}.");
        }

        return FromStorageDecimal(Convert.ToString(value, CultureInfo.InvariantCulture)!);
    }

    private static async Task WriteStockAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId<Product> productId,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        if (quantity < 0m)
        {
            throw new InvalidOperationException("Stock cannot become negative.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE stock
            SET quantity = $quantity
            WHERE product_id = $productId;
            """;
        command.Parameters.AddWithValue("$quantity", ToStorageDecimal(quantity));
        command.Parameters.AddWithValue("$productId", productId.ToString());

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException($"Stock update failed for product {productId}.");
        }
    }

    private static string ToStorageDecimal(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static decimal FromStorageDecimal(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
