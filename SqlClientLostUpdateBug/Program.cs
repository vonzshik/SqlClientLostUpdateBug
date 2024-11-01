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
            int successfulIterations = 0;

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
                catch (SqlException ex) when (ex.ErrorCode == unchecked((int)0x80131904))
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

                if (queryCancelled && afterUpdateValue == currentValue)
                {
                    successfulIterations++;

                    if (successfulIterations % 100 == 0)
                    {
                        Console.WriteLine($"Successful iterations: {successfulIterations}");
                    }
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
