using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using MySql.Data.MySqlClient; // MySQL client library
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Tasks = System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Storage.DataModel;
using SenseNet.ContentRepository.Storage.Schema;
using SenseNet.ContentRepository.Storage.Data.MySqlClient;
using SenseNet.Diagnostics;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    public partial class MySqlDataProvider : RelationalDataProviderBase
    {
        private DataOptions DataOptions { get; }
        private readonly MySqlDatabaseInstallationOptions _dbInstallerOptions;
        private readonly MySqlDatabaseInstaller _databaseInstaller;
        private IOptions<ConnectionStringOptions> ConnectionStrings { get; }
        private IDataInstaller DataInstaller { get; }
        private readonly ILogger _logger;
        private readonly IRetrier _retrier;

        public MySqlDataProvider(
            IOptions<DataOptions> dataOptions,
            IOptions<ConnectionStringOptions> connectionOptions,
            IOptions<MySqlDatabaseInstallationOptions> dbInstallerOptions,
            MySqlDatabaseInstaller databaseInstaller,
            IDataInstaller dataInstaller,
            ILogger<MySqlDataProvider> logger,
            IRetrier retrier)
        {
            DataInstaller = dataInstaller ?? throw new ArgumentNullException(nameof(dataInstaller));
            DataOptions = dataOptions.Value;
            _dbInstallerOptions = dbInstallerOptions.Value;
            _databaseInstaller = databaseInstaller;
            ConnectionStrings = connectionOptions;
            _logger = logger;
            _retrier = retrier;
        }

        public override SnDataContext CreateDataContext(CancellationToken token)
        {
            return new MySqlDataContext(ConnectionStrings.Value.Repository, DataOptions, _retrier, token);
        }

        /* =========================================================================================== Platform specific implementations */

        public override async Tasks.Task<IEnumerable<int>> QueryNodesByTypeAndPathAndNameAsync(
            int[] nodeTypeIds,
            string[] pathStart,
            bool orderByPath,
            string name,
            CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation(() =>
                $"MySqlDataProvider: QueryNodesByTypeAndPathAndNameAsync(nodeTypeIds: {nodeTypeIds.ToTrace()}, " +
                $"pathStart: {pathStart.ToTrace()}, orderByPath: {orderByPath}, name: {name})");

            var sql = new StringBuilder("SELECT NodeId FROM Nodes WHERE ");
            var first = true;

            if (pathStart != null && pathStart.Length > 0)
            {
                sql.AppendLine("(");
                for (int i = 0; i < pathStart.Length; i++)
                {
                    if (i > 0)
                        sql.AppendLine().Append(" OR ");
                    sql.Append(" Path LIKE @PathStart").Append(i);
                }
                sql.AppendLine(")");
                first = false;
            }

            if (!string.IsNullOrEmpty(name))
            {
                if (!first)
                    sql.Append(" AND ");
                sql.Append(" Name = @Name");
                first = false;
            }

            if (nodeTypeIds != null && nodeTypeIds.Length > 0)
            {
                if (!first)
                    sql.Append(" AND ");
                sql.Append(" NodeTypeId IN (").Append(string.Join(", ", nodeTypeIds)).Append(")");
            }

            if (orderByPath)
                sql.AppendLine().Append("ORDER BY Path");

            cancellationToken.ThrowIfCancellationRequested();

            using var ctx = (MySqlDataContext)CreateDataContext(cancellationToken);
            var result = await ctx.ExecuteReaderAsync(
                sql.ToString(),
                cmd =>
                {
                    if (pathStart != null)
                        for (int i = 0; i < pathStart.Length; i++)
                            cmd.Parameters.AddWithValue($"@PathStart{i}", $"{pathStart[i]}%");
                    if (!string.IsNullOrEmpty(name))
                        cmd.Parameters.AddWithValue("@Name", name);
                },
                async (reader, cancel) =>
                {
                    cancel.ThrowIfCancellationRequested();
                    var items = new List<int>();
                    while (await reader.ReadAsync(cancel).ConfigureAwait(false))
                    {
                        cancel.ThrowIfCancellationRequested();
                        items.Add(reader.GetInt32(0));
                    }
                    return (IEnumerable<int>)items;
                }).ConfigureAwait(false);

            op.Successful = true;
            return result;
        }

        public override async Tasks.Task InstallInitialDataAsync(InitialData data, CancellationToken cancellationToken)
        {
            await DataInstaller.InstallInitialDataAsync(data, this, cancellationToken).ConfigureAwait(false);
        }

        public override async Tasks.Task InstallDatabaseAsync(CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation("MySqlDataProvider: InstallDatabaseAsync().");

            if (!string.IsNullOrEmpty(_dbInstallerOptions.DatabaseName))
            {
                _logger.LogTrace($"Executing installer for database {_dbInstallerOptions.DatabaseName}.");

                await _databaseInstaller.InstallAsync().ConfigureAwait(false);

                await Tools.Retrier.RetryAsync(15, 2000, async () =>
                {
                    _logger.LogTrace("Trying to connect to the new database...");
                    using var ctx = CreateDataContext(cancellationToken);
                    await ctx.ExecuteNonQueryAsync("SELECT 1").ConfigureAwait(false);
                }, (i, ex) =>
                {
                    if (ex == null)
                    {
                        _logger.LogTrace("Successfully connected to the newly created database.");
                        return true;
                    }

                    if (i == 1)
                        _logger.LogError($"Could not connect to the database {_dbInstallerOptions.DatabaseName} after several retries.");

                    return false;
                }, cancellationToken);
            }
            else
            {
                _logger.LogTrace("Install database name is not configured. Proceeding with schema installation.");
            }

            _logger.LogTrace("Executing security schema script.");
            await ExecuteEmbeddedNonQueryScriptAsync(
                "SenseNet.ContentRepository.MySql.Scripts.MySqlInstall_Security.sql", cancellationToken)
                .ConfigureAwait(false);

            _logger.LogTrace("Executing database schema script.");
            await ExecuteEmbeddedNonQueryScriptAsync(
                "SenseNet.ContentRepository.MySql.Scripts.MySqlInstall_Schema.sql", cancellationToken)
                .ConfigureAwait(false);

            op.Successful = true;
        }

        private async Tasks.Task ExecuteEmbeddedNonQueryScriptAsync(string scriptName, CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation($"MySqlDataProvider: ExecuteEmbeddedNonQueryScript(scriptName: {scriptName})");

            using var stream = GetType().Assembly.GetManifestResourceStream(scriptName);
            if (stream == null)
                throw new InvalidOperationException($"Embedded resource {scriptName} not found.");

            using var sr = new StreamReader(stream);
            var script = await sr.ReadToEndAsync().ConfigureAwait(false);

            using var ctx = CreateDataContext(cancellationToken);
            await ctx.ExecuteNonQueryAsync(script);

            op.Successful = true;
        }

        // Other methods from MsSqlDataProvider should be ported similarly, ensuring compatibility with MySQL.
    }
}