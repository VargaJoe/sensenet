using System;
using System.Data;
using MySql.Data.MySqlClient; // MySQL client library
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenseNet.Tools.Configuration;

namespace SenseNet.Storage.Data.MySqlClient
{
    [Serializable]
    public class DbCreationException : Exception
    {
        public DbCreationException() { }
        public DbCreationException(string message) : base(message) { }
        public DbCreationException(string message, Exception inner) : base(message, inner) { }
        protected DbCreationException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Options for configuring database installation.
    /// </summary>
    [OptionsClass(sectionName: "sensenet:install:mysql")]
    public class MySqlDatabaseInstallationOptions
    {
        public string Server { get; set; }
        public string DatabaseName { get; set; }
        public string DbCreatorUserName { get; set; }
        public string DbCreatorPassword { get; set; }
        public string DbOwnerUserName { get; set; }
        public string DbOwnerPassword { get; set; }
    }

    public class MySqlDatabaseInstaller
    {
        private readonly ILogger<MySqlDatabaseInstaller> _logger;
        private readonly MySqlDatabaseInstallationOptions _options;

        public MySqlDatabaseInstaller(IOptions<MySqlDatabaseInstallationOptions> options, ILogger<MySqlDatabaseInstaller> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task InstallAsync()
        {
            ValidateParameters(_options);
            var targetConnectionString = GetConnectionString(_options);
            var rootConnectionString = new MySqlConnectionStringBuilder(targetConnectionString) { Database = "" }.ConnectionString;

            await EnsureDatabaseAsync(_options.DatabaseName, rootConnectionString).ConfigureAwait(false);
            await EnsureDatabaseUserAsync(_options.DbOwnerUserName, _options.DbOwnerPassword, rootConnectionString).ConfigureAwait(false);
            await GrantPrivilegesAsync(_options.DatabaseName, _options.DbOwnerUserName, rootConnectionString).ConfigureAwait(false);
        }

        public void ValidateParameters(MySqlDatabaseInstallationOptions options)
        {
            if (string.IsNullOrEmpty(options.DatabaseName))
                throw new ArgumentException("DatabaseName cannot be null or empty.");
        }

        public string GetConnectionString(MySqlDatabaseInstallationOptions options)
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = string.IsNullOrEmpty(options.Server) ? "localhost" : options.Server,
                UserID = options.DbCreatorUserName,
                Password = options.DbCreatorPassword,
                Database = options.DatabaseName
            };
            return builder.ConnectionString;
        }

        private async Task EnsureDatabaseAsync(string databaseName, string connectionString)
        {
            _logger.LogTrace($"Checking if database exists: {databaseName}");
            var databaseExists = await QueryDatabaseAsync(databaseName, connectionString).ConfigureAwait(false);
            if (!databaseExists)
                await CreateDatabaseAsync(databaseName, connectionString).ConfigureAwait(false);
        }

        private async Task<bool> QueryDatabaseAsync(string databaseName, string connectionString)
        {
            var query = $"SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = '{databaseName}'";
            var exists = false;
            await ExecuteSqlQueryAsync(query, connectionString, reader =>
            {
                exists = true;
                return false;
            }).ConfigureAwait(false);
            return exists;
        }

        public async Task CreateDatabaseAsync(string databaseName, string connectionString)
        {
            var createDbSql = $"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
            _logger.LogTrace($"Creating database: {databaseName}");
            try
            {
                await ExecuteSqlCommandAsync(createDbSql, connectionString).ConfigureAwait(false);
            }
            catch (MySqlException e)
            {
                if (e.Number == 1007) // Database already exists
                    return;
                throw new DbCreationException($"Cannot create database. {e.Message}", e);
            }
        }

        private async Task EnsureDatabaseUserAsync(string userName, string password, string connectionString)
        {
            _logger.LogTrace($"Ensuring database user exists: {userName}");
            var userExists = await QueryUserAsync(userName, connectionString).ConfigureAwait(false);
            if (!userExists)
                await CreateUserAsync(userName, password, connectionString).ConfigureAwait(false);
        }

        private async Task<bool> QueryUserAsync(string userName, string connectionString)
        {
            var query = $"SELECT User FROM mysql.user WHERE User = '{userName}'";
            var exists = false;
            await ExecuteSqlQueryAsync(query, connectionString, reader =>
            {
                exists = true;
                return false;
            }).ConfigureAwait(false);
            return exists;
        }

        public async Task CreateUserAsync(string userName, string password, string connectionString)
        {
            var createUserSql = $"CREATE USER '{userName}'@'%' IDENTIFIED BY '{password}'";
            try
            {
                await ExecuteSqlCommandAsync(createUserSql, connectionString).ConfigureAwait(false);
            }
            catch (MySqlException e)
            {
                if (e.Number == 1396) // User already exists
                    return;
                throw new DbCreationException($"Cannot create database user. {e.Message}", e);
            }
        }

        private async Task GrantPrivilegesAsync(string databaseName, string userName, string connectionString)
        {
            var grantPrivilegesSql = $"GRANT ALL PRIVILEGES ON `{databaseName}`.* TO '{userName}'@'%'";
            try
            {
                await ExecuteSqlCommandAsync(grantPrivilegesSql, connectionString).ConfigureAwait(false);
                await ExecuteSqlCommandAsync("FLUSH PRIVILEGES", connectionString).ConfigureAwait(false);
            }
            catch (MySqlException e)
            {
                throw new DbCreationException($"Cannot grant privileges to user. {e.Message}", e);
            }
        }

        private async Task ExecuteSqlCommandAsync(string sql, string connectionString)
        {
            using (var connection = new MySqlConnection(connectionString))
            using (var command = new MySqlCommand(sql, connection))
            {
                command.CommandType = CommandType.Text;
                await connection.OpenAsync().ConfigureAwait(false);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private async Task ExecuteSqlQueryAsync(string sql, string connectionString, Func<MySqlDataReader, bool> processRow)
        {
            using (var connection = new MySqlConnection(connectionString))
            using (var command = new MySqlCommand(sql, connection))
            {
                command.CommandType = CommandType.Text;
                await connection.OpenAsync().ConfigureAwait(false);
                using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                    while (await reader.ReadAsync().ConfigureAwait(false) && processRow(reader)) ;
            }
        }
    }
}