using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient; // MySQL connector
using SenseNet.Configuration;
using SenseNet.ContentRepository.Storage.DataModel;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    public class MySqlClientStoreDataProvider : IClientStoreDataProvider
    {
        private readonly string _connectionString;

        public MySqlClientStoreDataProvider(IOptions<ConnectionStringOptions> connectionOptions)
        {
            _connectionString = connectionOptions.Value.Repository;
        }

        public async Task<Client[]> LoadClientsByRepositoryAsync(string repositoryHost, CancellationToken cancellationToken)
        {
            var clients = new List<Client>();
            var query = @"
                SELECT * FROM ClientApps WHERE Repository = @Repository;
                SELECT S.* FROM ClientSecrets S JOIN ClientApps A ON S.ClientId = A.ClientId WHERE A.Repository = @Repository;
            ";

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Repository", repositoryHost);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            // Read ClientApps
            while (await reader.ReadAsync(cancellationToken))
            {
                clients.Add(new Client
                {
                    ClientId = reader.GetString("ClientId"),
                    Name = reader.GetString("Name"),
                    Repository = reader.GetString("Repository"),
                    UserName = reader.GetString("UserName"),
                    Authority = reader.GetString("Authority"),
                    Type = (ClientType)reader.GetInt32("Type"),
                });
            }

            // Move to ClientSecrets result set
            await reader.NextResultAsync(cancellationToken);

            // Read ClientSecrets
            while (await reader.ReadAsync(cancellationToken))
            {
                var clientId = reader.GetString("ClientId");
                var client = clients.FirstOrDefault(c => c.ClientId == clientId);
                if (client != null)
                {
                    client.Secrets.Add(new ClientSecret
                    {
                        Id = reader.GetString("Id"),
                        Value = reader.GetString("Value"),
                        CreationDate = reader.GetDateTime("CreationDate"),
                        ValidTill = reader.GetDateTime("ValidTill"),
                    });
                }
            }

            return clients.ToArray();
        }

        public async Task SaveClientAsync(Client client, CancellationToken cancellationToken)
        {
            var query = @"
                INSERT INTO ClientApps (ClientId, Name, Repository, UserName, Authority, Type)
                VALUES (@ClientId, @Name, @Repository, @UserName, @Authority, @Type)
                ON DUPLICATE KEY UPDATE
                    Name = @Name, Repository = @Repository, UserName = @UserName, Authority = @Authority, Type = @Type;

                DELETE FROM ClientSecrets WHERE ClientId = @ClientId;

                INSERT INTO ClientSecrets (Id, ClientId, Value, CreationDate, ValidTill)
                VALUES (@SecretId, @ClientId, @Value, @CreationDate, @ValidTill);
            ";

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@ClientId", client.ClientId);
            command.Parameters.AddWithValue("@Name", client.Name);
            command.Parameters.AddWithValue("@Repository", client.Repository);
            command.Parameters.AddWithValue("@UserName", client.UserName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@Authority", client.Authority);
            command.Parameters.AddWithValue("@Type", (int)client.Type);

            // Execute the insert/update for ClientApps
            await command.ExecuteNonQueryAsync(cancellationToken);

            foreach (var secret in client.Secrets)
            {
                // Update parameters for ClientSecrets
                command.Parameters.Clear();
                command.Parameters.AddWithValue("@SecretId", secret.Id);
                command.Parameters.AddWithValue("@ClientId", client.ClientId);
                command.Parameters.AddWithValue("@Value", secret.Value);
                command.Parameters.AddWithValue("@CreationDate", secret.CreationDate);
                command.Parameters.AddWithValue("@ValidTill", secret.ValidTill);

                // Execute the insert for ClientSecrets
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}