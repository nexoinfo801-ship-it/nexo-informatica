using System.Globalization;
using Microsoft.Data.Sqlite;
using Playloud.Domain.Cash;
using Playloud.Domain.Catalog;
using Playloud.Domain.Common;
using Playloud.Domain.Payments;
using Playloud.Domain.Sales;

namespace Playloud.Persistence.Sqlite;

public sealed record SqliteHealth(bool ForeignKeysEnabled, string JournalMode);

public sealed record StoredProduct(
    EntityId<Product> Id,
    string Name,
    decimal UnitPrice,
    decimal UnitCost);

public sealed record StoredProductSearchResult(
    EntityId<Product> Id,
    string Name,
    decimal UnitPrice,
    decimal AvailableStock);

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

            CREATE TABLE IF NOT EXISTS cash_sessions (
                id TEXT PRIMARY KEY NOT NULL,
                business_date TEXT NOT NULL,
                opening_balance TEXT NOT NULL,
                is_open INTEGER NOT NULL CHECK (is_open IN (0, 1))
            );

            CREATE TABLE IF NOT EXISTS payments (
                id TEXT PRIMARY KEY NOT NULL,
                sale_id TEXT NOT NULL,
                method INTEGER NOT NULL,
                amount TEXT NOT NULL,
                FOREIGN KEY (sale_id) REFERENCES sales(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS cash_movements (
                id TEXT PRIMARY KEY NOT NULL,
                cash_session_id TEXT NOT NULL,
                sale_id TEXT NOT NULL,
                amount TEXT NOT NULL,
                FOREIGN KEY (cash_session_id) REFERENCES cash_sessions(id) ON DELETE RESTRICT,
                FOREIGN KEY (sale_id) REFERENCES sales(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS cash_adjustments (
                id TEXT PRIMARY KEY NOT NULL,
                cash_session_id TEXT NOT NULL,
                kind INTEGER NOT NULL,
                amount TEXT NOT NULL,
                reason TEXT NOT NULL,
                FOREIGN KEY (cash_session_id) REFERENCES cash_sessions(id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS cash_closings (
                cash_session_id TEXT PRIMARY KEY NOT NULL,
                expected_cash TEXT NOT NULL,
                actual_cash TEXT NOT NULL,
                difference TEXT NOT NULL,
                FOREIGN KEY (cash_session_id) REFERENCES cash_sessions(id) ON DELETE RESTRICT
            );

            PRAGMA user_version=3;
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

    public async Task SaveCashSessionAsync(
        CashSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cash_sessions (id, business_date, opening_balance, is_open)
            VALUES ($id, $businessDate, $openingBalance, $isOpen);
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString());
        command.Parameters.AddWithValue(
            "$businessDate",
            session.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$openingBalance", ToStorageDecimal(session.OpeningBalance));
        command.Parameters.AddWithValue("$isOpen", session.IsOpen ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveCashAdjustmentAsync(
        EntityId<CashSession> cashSessionId,
        CashMovement movement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movement);

        if (movement.Kind is not (CashMovementKind.Supply or CashMovementKind.Withdrawal))
        {
            throw new InvalidOperationException("Only manual cash adjustments can be persisted with this operation.");
        }

        if (movement.SaleId is not null)
        {
            throw new InvalidOperationException("Manual cash adjustments cannot reference a sale.");
        }

        if (string.IsNullOrWhiteSpace(movement.Reason))
        {
            throw new InvalidOperationException("Cash adjustment reason is required.");
        }

        if (movement.Kind == CashMovementKind.Supply && movement.Amount <= 0m)
        {
            throw new InvalidOperationException("Supply amount must be positive.");
        }

        if (movement.Kind == CashMovementKind.Withdrawal && movement.Amount >= 0m)
        {
            throw new InvalidOperationException("Withdrawal amount must be negative.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            var session = await ReadCashSessionAsync(
                connection,
                transaction,
                cashSessionId,
                cancellationToken);

            if (!session.IsOpen)
            {
                throw new InvalidOperationException("Cash session is closed.");
            }

            if (movement.Kind == CashMovementKind.Withdrawal)
            {
                var expectedBalance = await ReadExpectedCashBalanceAsync(
                    connection,
                    transaction,
                    cashSessionId,
                    cancellationToken);

                if (expectedBalance + movement.Amount < 0m)
                {
                    throw new InvalidOperationException("Withdrawal cannot make expected cash negative.");
                }
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cash_adjustments (id, cash_session_id, kind, amount, reason)
                VALUES ($id, $cashSessionId, $kind, $amount, $reason);
                """;
            command.Parameters.AddWithValue("$id", movement.Id.ToString());
            command.Parameters.AddWithValue("$cashSessionId", cashSessionId.ToString());
            command.Parameters.AddWithValue("$kind", (int)movement.Kind);
            command.Parameters.AddWithValue("$amount", ToStorageDecimal(movement.Amount));
            command.Parameters.AddWithValue("$reason", movement.Reason);
            await command.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<CashMovement>> GetCashAdjustmentsAsync(
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, amount, reason
            FROM cash_adjustments
            WHERE cash_session_id = $cashSessionId
            ORDER BY rowid;
            """;
        command.Parameters.AddWithValue("$cashSessionId", cashSessionId.ToString());

        var result = new List<CashMovement>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CashMovement(
                new EntityId<CashMovement>(Guid.Parse(reader.GetString(0))),
                (CashMovementKind)reader.GetInt32(1),
                null,
                FromStorageDecimal(reader.GetString(2)),
                reader.GetString(3)));
        }

        return result;
    }

    public async Task CloseCashSessionAsync(
        EntityId<CashSession> cashSessionId,
        CashClosing closing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(closing);

        if (closing.ActualCash < 0m)
        {
            throw new InvalidOperationException("Actual cash cannot be negative.");
        }

        if (closing.Difference != closing.ActualCash - closing.ExpectedCash)
        {
            throw new InvalidOperationException("Cash closing difference is inconsistent.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            var session = await ReadCashSessionAsync(
                connection,
                transaction,
                cashSessionId,
                cancellationToken);

            if (!session.IsOpen)
            {
                throw new InvalidOperationException("Cash session is already closed.");
            }

            var persistedExpected = await ReadExpectedCashBalanceAsync(
                connection,
                transaction,
                cashSessionId,
                cancellationToken);

            if (persistedExpected != closing.ExpectedCash)
            {
                throw new InvalidOperationException("Cash closing expected balance does not match persisted ledger.");
            }

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO cash_closings (
                        cash_session_id, expected_cash, actual_cash, difference)
                    VALUES (
                        $cashSessionId, $expectedCash, $actualCash, $difference);
                    """;
                insert.Parameters.AddWithValue("$cashSessionId", cashSessionId.ToString());
                insert.Parameters.AddWithValue("$expectedCash", ToStorageDecimal(closing.ExpectedCash));
                insert.Parameters.AddWithValue("$actualCash", ToStorageDecimal(closing.ActualCash));
                insert.Parameters.AddWithValue("$difference", ToStorageDecimal(closing.Difference));
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cash_sessions
                    SET is_open = 0
                    WHERE id = $id AND is_open = 1;
                    """;
                update.Parameters.AddWithValue("$id", cashSessionId.ToString());
                var affected = await update.ExecuteNonQueryAsync(cancellationToken);
                if (affected != 1)
                {
                    throw new InvalidOperationException("Cash session could not be closed.");
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> IsCashSessionOpenAsync(
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT is_open FROM cash_sessions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", cashSessionId.ToString());

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull)
        {
            throw new KeyNotFoundException($"Cash session {cashSessionId} was not found.");
        }

        return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
    }

    public async Task<CashClosing?> GetCashClosingAsync(
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT expected_cash, actual_cash, difference
            FROM cash_closings
            WHERE cash_session_id = $cashSessionId;
            """;
        command.Parameters.AddWithValue("$cashSessionId", cashSessionId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CashClosing(
            FromStorageDecimal(reader.GetString(0)),
            FromStorageDecimal(reader.GetString(1)),
            FromStorageDecimal(reader.GetString(2)));
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

    public async Task<IReadOnlyList<StoredProductSearchResult>> SearchProductsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Search result limit must be between 1 and 100.");
        }

        var escapedQuery = EscapeLikePattern(query.Trim());

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.name, p.unit_price, s.quantity
            FROM products AS p
            INNER JOIN stock AS s ON s.product_id = p.id
            WHERE p.name LIKE $contains ESCAPE '\' COLLATE NOCASE
            ORDER BY
                CASE
                    WHEN p.name LIKE $startsWith ESCAPE '\' COLLATE NOCASE THEN 0
                    ELSE 1
                END,
                p.name COLLATE NOCASE,
                p.id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$contains", $"%{escapedQuery}%");
        command.Parameters.AddWithValue("$startsWith", $"{escapedQuery}%");
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<StoredProductSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new StoredProductSearchResult(
                new EntityId<Product>(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                FromStorageDecimal(reader.GetString(2)),
                FromStorageDecimal(reader.GetString(3))));
        }

        return results;
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

    public async Task CommitSaleAsync(
        Sale sale,
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sale);

        if (!sale.IsCompleted)
        {
            throw new InvalidOperationException("Only completed sales can be persisted.");
        }

        if (sale.Lines.Count == 0)
        {
            throw new InvalidOperationException("A sale must contain at least one line.");
        }

        if (sale.Payments.Count == 0)
        {
            throw new InvalidOperationException("A completed sale must contain at least one payment.");
        }

        if (sale.Payments.Sum(static payment => payment.Amount) != sale.GrossTotal)
        {
            throw new InvalidOperationException("Payment total does not match sale total.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            var cashSession = await ReadCashSessionAsync(
                connection,
                transaction,
                cashSessionId,
                cancellationToken);

            if (!cashSession.IsOpen)
            {
                throw new InvalidOperationException("Cash session is closed.");
            }

            if (cashSession.BusinessDate != sale.BusinessDate)
            {
                throw new InvalidOperationException("Sale business date does not match cash session date.");
            }

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

            foreach (var payment in sale.Payments)
            {
                await InsertPaymentAsync(connection, transaction, sale.Id, payment, cancellationToken);

                if (payment.Method == PaymentMethod.Cash)
                {
                    await InsertCashMovementAsync(
                        connection,
                        transaction,
                        cashSessionId,
                        sale.Id,
                        payment.Amount,
                        cancellationToken);
                }
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

    public async Task<int> CountPaymentsAsync(
        EntityId<Sale> saleId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM payments WHERE sale_id = $saleId;";
        command.Parameters.AddWithValue("$saleId", saleId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task<int> CountCashMovementsAsync(
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM cash_movements WHERE cash_session_id = $cashSessionId;";
        command.Parameters.AddWithValue("$cashSessionId", cashSessionId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task<decimal> GetExpectedCashBalanceAsync(
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        return await ReadExpectedCashBalanceAsync(
            connection,
            transaction,
            cashSessionId,
            cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var configure = connection.CreateCommand();
        configure.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await configure.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }

    private static async Task<(DateOnly BusinessDate, bool IsOpen)> ReadCashSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT business_date, is_open
            FROM cash_sessions
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", cashSessionId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Cash session {cashSessionId} was not found.");
        }

        var businessDate = DateOnly.ParseExact(
            reader.GetString(0),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
        var isOpen = reader.GetInt32(1) == 1;
        return (businessDate, isOpen);
    }

    private static async Task<decimal> ReadExpectedCashBalanceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken)
    {
        await using var sessionCommand = connection.CreateCommand();
        sessionCommand.Transaction = transaction;
        sessionCommand.CommandText = "SELECT opening_balance FROM cash_sessions WHERE id = $id;";
        sessionCommand.Parameters.AddWithValue("$id", cashSessionId.ToString());
        var openingValue = await sessionCommand.ExecuteScalarAsync(cancellationToken);
        if (openingValue is null || openingValue is DBNull)
        {
            throw new InvalidOperationException($"Cash session {cashSessionId} was not found.");
        }

        var balance = FromStorageDecimal(
            Convert.ToString(openingValue, CultureInfo.InvariantCulture)!);

        balance += await SumCashTableAsync(
            connection,
            transaction,
            "cash_movements",
            cashSessionId,
            cancellationToken);
        balance += await SumCashTableAsync(
            connection,
            transaction,
            "cash_adjustments",
            cashSessionId,
            cancellationToken);

        return balance;
    }

    private static async Task<decimal> SumCashTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        EntityId<CashSession> cashSessionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT amount FROM {tableName} WHERE cash_session_id = $cashSessionId;";
        command.Parameters.AddWithValue("$cashSessionId", cashSessionId.ToString());

        var total = 0m;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            total += FromStorageDecimal(reader.GetString(0));
        }

        return total;
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
        command.Parameters.AddWithValue(
            "$businessDate",
            sale.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
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

    private static async Task InsertPaymentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId<Sale> saleId,
        Payment payment,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO payments (id, sale_id, method, amount)
            VALUES ($id, $saleId, $method, $amount);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$saleId", saleId.ToString());
        command.Parameters.AddWithValue("$method", (int)payment.Method);
        command.Parameters.AddWithValue("$amount", ToStorageDecimal(payment.Amount));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCashMovementAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EntityId<CashSession> cashSessionId,
        EntityId<Sale> saleId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cash_movements (id, cash_session_id, sale_id, amount)
            VALUES ($id, $cashSessionId, $saleId, $amount);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$cashSessionId", cashSessionId.ToString());
        command.Parameters.AddWithValue("$saleId", saleId.ToString());
        command.Parameters.AddWithValue("$amount", ToStorageDecimal(amount));
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
