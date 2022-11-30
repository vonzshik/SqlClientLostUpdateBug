using Microsoft.Data.SqlClient;

namespace SqlClientLostUpdateBug
{
    internal class Program
    {
        const string ConnectionString = "Server=.; Database=SomeDatabase; User ID=SomeUser; Password=SomePassword; Pooling=true; Max Pool Size=200; Trust Server Certificate=true";

        static async Task Main(string[] args)
        {
            await RecreateTableAsync();

            var currentValue = await GetCurrentValueAsync();

            while (true)
            {
                using var cts = new CancellationTokenSource();

                var queryCancelled = false;
                SqlException? queryCancelledException = null;

                try
                {
                    await using var conn = new SqlConnection(ConnectionString);
                    await conn.OpenAsync();

                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE TestTable SET Value = Value + 1";
                    var task = cmd.ExecuteNonQueryAsync(cts.Token);
                    cts.Cancel();
                    await task;
                }
                catch (SqlException ex)
                {
                    queryCancelled = true;
                    queryCancelledException = ex;
                }

                var afterUpdateValue = await GetCurrentValueAsync();
                if (queryCancelled && afterUpdateValue != currentValue)
                {
                    Console.WriteLine($"Query cancelled but the value is changed from {currentValue} to {afterUpdateValue}");
                    Console.WriteLine("Query was cancelled with exception:");
                    Console.WriteLine(queryCancelledException);
                }

                currentValue = afterUpdateValue;
            }
        }

        static async Task<int> GetCurrentValueAsync()
        {
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT TOP(1) Value FROM TestTable";
            return (int)await cmd.ExecuteScalarAsync();
        }

        static async Task RecreateTableAsync()
        {
            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                @"
IF OBJECT_ID(N'dbo.TestTable', N'U') IS NOT NULL
    DROP TABLE TestTable;

CREATE TABLE TestTable(Value int);

INSERT INTO TestTable VALUES(0);
                ";

            await cmd.ExecuteNonQueryAsync();
        }
    }
}