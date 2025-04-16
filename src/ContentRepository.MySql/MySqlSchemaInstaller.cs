using System;
using System.Data;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using SenseNet.ContentRepository.Storage.DataModel;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    public class MySqlSchemaInstaller
    {
        private readonly string _connectionString;

        public MySqlSchemaInstaller(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task InstallSchemaAsync(RepositorySchemaData schema)
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                foreach (var script in schema.Scripts)
                {
                    using var command = new MySqlCommand(script, connection, transaction);
                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}