using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using MySql.Data.MySqlClient; // Use MySQL client library
using System.Linq;
using System.Threading;
using Tasks = System.Threading.Tasks;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.Diagnostics;

namespace SenseNet.ContentRepository.Security.Clients
{
    /// <summary>
    /// MySQL implementation of the <see cref="IClientStoreDataProvider"/> interface.
    /// </summary>
    public class MySqlClientStoreDataProvider : IClientStoreDataProvider
    {
        #region Create scripts
        public static readonly string DropAndCreateTablesSql = @"
            CREATE TABLE IF NOT EXISTS `ClientApps` (
                `ClientId` VARCHAR(50) NOT NULL,
                `Name` NVARCHAR(450),
                `Repository` NVARCHAR(450),
                `UserName` NVARCHAR(450),
                `Authority` NVARCHAR(450),
                `Type` INT,
                PRIMARY KEY (`ClientId`)
            );

            CREATE TABLE IF NOT EXISTS `ClientSecrets` (
                `Id` VARCHAR(50) NOT NULL,
                `ClientId` VARCHAR(50) NOT NULL,
                `Value` NVARCHAR(450) NOT NULL,
                `CreationDate` DATETIME NOT NULL,
                `ValidTill` DATETIME NOT NULL,
                PRIMARY KEY (`Id`),
                FOREIGN KEY (`ClientId`) REFERENCES `ClientApps` (`ClientId`)
            );

            CREATE INDEX IF NOT EXISTS `IX_ClientApps_Authority` ON `ClientApps` (`Authority`);
            CREATE INDEX IF NOT EXISTS `IX_ClientApps_Repository` ON `ClientApps` (`Repository`);
            CREATE INDEX IF NOT EXISTS `IX_ClientSecrets_ClientId` ON `ClientSecrets` (`ClientId`);
        ";
        #endregion

        private RelationalDataProviderBase DataProvider => (RelationalDataProviderBase)Providers.Instance.DataProvider;

        /* =============================================================================================== LOAD */

        private static readonly string LoadClientsByRepositorySql = @"
            SELECT * FROM `ClientApps` WHERE `Repository` = @Repository;
            SELECT S.* FROM `ClientSecrets` S 
            JOIN `ClientApps` A ON S.`ClientId` = A.`ClientId` 
            WHERE A.`Repository` = @Repository;
        ";

        public async Tasks.Task<Client[]> LoadClientsByRepositoryAsync(string repositoryHost, CancellationToken cancellation)
        {
            using var op = SnTrace.Database.StartOperation("MySqlClientStoreDataProvider: " +
                "LoadClientsByRepository(repositoryHost: {0})", repositoryHost);

            using var ctx = DataProvider.CreateDataContext(cancellation);

            try
            {
                var result = await ctx.ExecuteReaderAsync(LoadClientsByRepositorySql, cmd =>
                {
                    cmd.Parameters.Add(ctx.CreateParameter("@Repository", DbType.String, 450, repositoryHost));
                },
                async (reader, cancel) => await GetClientsFromReader(reader, cancel)).ConfigureAwait(false);
                op.Successful = true;

                return result;
            }
            catch (MySqlException ex)
            {
                throw new DataException("Error loading clients by repository. " + ex.Message, ex);
            }
        }

        private async Tasks.Task<Client[]> GetClientsFromReader(DbDataReader reader, CancellationToken cancel)
        {
            var clients = new List<Client>();
            while (await reader.ReadAsync(cancel).ConfigureAwait(false))
            {
                cancel.ThrowIfCancellationRequested();
                clients.Add(new Client
                {
                    ClientId = reader.GetSafeString(reader.GetOrdinal("ClientId")),
                    Name = reader.GetSafeString(reader.GetOrdinal("Name")),
                    Repository = reader.GetSafeString(reader.GetOrdinal("Repository")),
                    UserName = reader.GetSafeString(reader.GetOrdinal("UserName")),
                    Authority = reader.GetSafeString(reader.GetOrdinal("Authority")),
                    Type = (ClientType)reader.GetInt32(reader.GetOrdinal("Type")),
                });
            }
            await reader.NextResultAsync(cancel);
            while (await reader.ReadAsync(cancel).ConfigureAwait(false))
            {
                cancel.ThrowIfCancellationRequested();
                var clientId = reader.GetString(reader.GetOrdinal("ClientId"));
                var client = clients.First(x => x.ClientId == clientId);
                client.Secrets.Add(new ClientSecret
                {
                    Id = reader.GetString(reader.GetOrdinal("Id")),
                    Value = reader.GetString(reader.GetOrdinal("Value")),
                    CreationDate = reader.GetDateTime(reader.GetOrdinal("CreationDate")),
                    ValidTill = reader.GetDateTime(reader.GetOrdinal("ValidTill")),
                });
            }

            return clients.ToArray();
        }

        /* =============================================================================================== SAVE */

        private static readonly string UpsertClientSql = @"
            INSERT INTO `ClientApps` (`ClientId`, `Name`, `Repository`, `UserName`, `Authority`, `Type`)
            VALUES (@ClientId, @Name, @Repository, @UserName, @Authority, @Type)
            ON DUPLICATE KEY UPDATE
                `Name` = @Name, `Repository` = @Repository, `UserName` = @UserName, `Authority` = @Authority, `Type` = @Type;
        ";

        private static readonly string DeleteSecretSql = @"
            DELETE FROM `ClientSecrets` WHERE `ClientId` = @ClientId;
        ";

        public async Tasks.Task SaveClientAsync(Client client, CancellationToken cancellation)
        {
            using var op = SnTrace.Database.StartOperation("MySqlClientStoreDataProvider: " +
                "SaveClient: ClientId/Name: {0}, Repository: {1}, UserName: {2}, Authority: {3}, Type: {4}({5})",
                client?.ClientId, client?.Repository, client?.UserName,
                client?.Authority, client?.Type, (int)(client?.Type ?? 0));

            using var ctx = ((RelationalDataProviderBase)Providers.Instance.DataProvider).CreateDataContext(cancellation);
            using var transaction = ctx.BeginTransaction();
            // UPSERT CLIENT
            await ctx.ExecuteNonQueryAsync(UpsertClientSql, cmd =>
            {
                cmd.Parameters.Add(ctx.CreateParameter("@ClientId", DbType.String, 50, client.ClientId));
                cmd.Parameters.Add(ctx.CreateParameter("@Name", DbType.String, 450, client.Name ?? client.ClientId));
                cmd.Parameters.Add(ctx.CreateParameter("@Repository", DbType.String, 450, client.Repository));
                cmd.Parameters.Add(ctx.CreateParameter("@UserName", DbType.String, 450,
                    (object)client.UserName ?? DBNull.Value));
                cmd.Parameters.Add(ctx.CreateParameter("@Authority", DbType.String, 450, client.Authority));
                cmd.Parameters.Add(ctx.CreateParameter("@Type", DbType.Int32, (int)client.Type));
            }).ConfigureAwait(false);

            // DELETE ALL RELATED SECRETS
            await ctx.ExecuteNonQueryAsync(DeleteSecretSql, cmd =>
            {
                cmd.Parameters.Add(ctx.CreateParameter("@ClientId", DbType.String, 50, client.ClientId));
            }).ConfigureAwait(false);

            // INSERT SECRETS
            foreach (var secret in client.Secrets)
                await SaveSecretAsync(client.ClientId, secret, false, ctx);

            transaction.Commit();
            op.Successful = true;
        }

        private async Tasks.Task SaveSecretAsync(string clientId, ClientSecret secret, bool deleteBefore, DataContext ctx)
        {
            var sql = deleteBefore
                ? @"
                    DELETE FROM `ClientSecrets` WHERE `Id` = @Id;
                    INSERT INTO `ClientSecrets` (`Id`, `ClientId`, `Value`, `CreationDate`, `ValidTill`)
                    VALUES (@Id, @ClientId, @Value, @CreationDate, @ValidTill);
                "
                : @"
                    INSERT INTO `ClientSecrets` (`Id`, `ClientId`, `Value`, `CreationDate`, `ValidTill`)
                    VALUES (@Id, @ClientId, @Value, @CreationDate, @ValidTill);
                ";
            using var op = SnTrace.Database.StartOperation("MySqlClientStoreDataProvider: " +
                "SaveSecret: ClientId: {0}, Id: {1}, Value: {2}, CreationDate: {3}, Valid*
