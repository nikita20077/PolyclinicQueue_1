using MySql.Data.MySqlClient;

namespace PolyclinicQueue.API.Data;

/// <summary>
/// Обёртка над MySql.Data. Все методы принимают SQL с параметрами (@name) —
/// значения никогда не подставляются в строку запроса напрямую.
/// </summary>
public class DatabaseHelper
{
    private readonly string _connectionString;

    public DatabaseHelper(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Не найдена строка подключения DefaultConnection в appsettings.json");
    }

    public MySqlConnection CreateConnection() => new(_connectionString);

    /// <summary>Создаёт параметр; null превращается в DBNull.</summary>
    public static MySqlParameter P(string name, object? value) =>
        new(name, value ?? DBNull.Value);

    /// <summary>Выполняет команду внутри транзакции. Для INSERT возвращает LastInsertedId.</summary>
    public static async Task<int> ExecTxAsync(MySqlConnection conn, MySqlTransaction tx, string sql, params MySqlParameter[] parameters)
    {
        await using var cmd = new MySqlCommand(sql, conn, tx);
        cmd.Parameters.AddRange(parameters);
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>SELECT, возвращающий список объектов. map превращает строку в объект.</summary>
    public async Task<List<T>> QueryAsync<T>(string sql, Func<MySqlDataReader, T> map, params MySqlParameter[] parameters)
    {
        var result = new List<T>();
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters);
        await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(map(reader));
        return result;
    }

    /// <summary>SELECT одной записи; null, если ничего не найдено.</summary>
    public async Task<T?> QuerySingleAsync<T>(string sql, Func<MySqlDataReader, T> map, params MySqlParameter[] parameters)
        where T : class
    {
        var list = await QueryAsync(sql, map, parameters);
        return list.FirstOrDefault();
    }

    /// <summary>INSERT / UPDATE / DELETE. Возвращает число затронутых строк.</summary>
    public async Task<int> ExecuteAsync(string sql, params MySqlParameter[] parameters)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters);
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>INSERT с возвратом Id новой записи (LastInsertedId).</summary>
    public async Task<int> InsertAsync(string sql, params MySqlParameter[] parameters)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters);
        await cmd.ExecuteNonQueryAsync();
        return (int)cmd.LastInsertedId;
    }

    /// <summary>Возвращает первое значение первой строки (COUNT, MAX и т.п.).</summary>
    public async Task<object?> ScalarAsync(string sql, params MySqlParameter[] parameters)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddRange(parameters);
        var value = await cmd.ExecuteScalarAsync();
        return value == DBNull.Value ? null : value;
    }

    /// <summary>Удобный COUNT(*) -> int.</summary>
    public async Task<int> CountAsync(string sql, params MySqlParameter[] parameters)
    {
        var value = await ScalarAsync(sql, parameters);
        return value is null ? 0 : Convert.ToInt32(value);
    }

    /// <summary>
    /// Выполняет несколько действий в одной транзакции (например: создать User + Doctor).
    /// Если действие бросает исключение — откат.
    /// </summary>
    public async Task<T> InTransactionAsync<T>(Func<MySqlConnection, MySqlTransaction, Task<T>> action)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            var result = await action(conn, tx);
            await tx.CommitAsync();
            return result;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}

/// <summary>Безопасное чтение значений из MySqlDataReader (устойчиво к разным типам столбцов).</summary>
public static class ReaderExtensions
{
    public static string? GetStringOrNull(this MySqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
    }

    public static DateTime? GetDateTimeOrNull(this MySqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));
    }

    public static double? GetDoubleOrNull(this MySqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : Convert.ToDouble(r.GetValue(i));
    }

    public static int? GetIntOrNull(this MySqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    }

    public static int GetInt(this MySqlDataReader r, string column) => Convert.ToInt32(r[column]);
    public static string GetStr(this MySqlDataReader r, string column) => r.GetStringOrNull(column) ?? string.Empty;
    public static DateTime GetDate(this MySqlDataReader r, string column) => Convert.ToDateTime(r[column]);
    public static bool GetBool(this MySqlDataReader r, string column) => Convert.ToBoolean(r[column]);
}
