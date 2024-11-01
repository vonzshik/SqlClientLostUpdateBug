using Microsoft.Data.SqlClient;

namespace SqlClientLostUpdateBug
{
    internal class Program
    {
        const string ConnectionString = "Server=.; Database=SomeDatabase; User ID=SomeUser; Password=SomePassword; Pooling=true; Max Pool Size=200; Trust Server Certificate=true";

                static async Task Main(string[] args)
        {
            Console.WriteLine($".NET Version: {Environment.Version}");
            var version = Assembly.GetExecutingAssembly().GetReferencedAssemblies().First(x => x.Name.Contains("Microsoft.Data.Sql")).Version;
            Console.WriteLine($"MDS Version: {version}");
            await RecreateTableAsync();

            var currentValue = await GetCurrentValueAsync();
            int successfulIterations = 0;
            int failedIterations = 0;

            await using var conn = new SqlConnection(ConnectionString);
            await conn.OpenAsync();

            while (true)
            {
                using var cts = new CancellationTokenSource();

                var queryCancelled = false;
                SqlException? queryCancelledException = null;

                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE TestTable SET Value = Value + 1, Mark = " + (successfulIterations + failedIterations);
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
                    Console.WriteLine($"Iteration that failed: {successfulIterations + failedIterations}");
                    failedIterations++;

                    //Console.WriteLine($"Query cancelled but the value is changed from {currentValue} to {afterUpdateValue}");
                    //Console.WriteLine("Query was cancelled with exception:");
                    //Console.WriteLine(queryCancelledException);
                }

                if (queryCancelled && afterUpdateValue == currentValue)
                {
                    successfulIterations++;

                    
                }

                if ((successfulIterations + failedIterations) % 100 == 0)
                {
                    Console.WriteLine($"Successful iterations: {successfulIterations} Failed Iterations: {failedIterations}");
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

CREATE TABLE TestTable(Value int, Mark int);

INSERT INTO TestTable VALUES (0, 0);
                ";

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
