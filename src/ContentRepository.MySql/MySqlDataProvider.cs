using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient; // MySQL connector
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Storage.DataModel;
using SenseNet.ContentRepository.Storage.Schema;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    public class MySqlDataProvider : RelationalDataProviderBase
    {
        private DataOptions DataOptions { get; }
        private IOptions<ConnectionStringOptions> ConnectionStrings { get; }
        private readonly ILogger _logger;

        public MySqlDataProvider(IOptions<DataOptions> dataOptions, IOptions<ConnectionStringOptions> connectionOptions,
            ILogger<MySqlDataProvider> logger)
        {
            DataOptions = dataOptions.Value;
            ConnectionStrings = connectionOptions;
            _logger = logger;
        }

        public override SnDataContext CreateDataContext(CancellationToken token)
        {
            return new MySqlDataContext(ConnectionStrings.Value.Repository, DataOptions, token);
        }

        public override async Task<IEnumerable<int>> QueryNodesByTypeAndPathAndNameAsync(
            int[] nodeTypeIds, string[] pathStart, bool orderByPath, string name,
            CancellationToken cancellationToken)
        {
            var sql = new StringBuilder("SELECT NodeId FROM Nodes WHERE ");
            var first = true;

            if (pathStart != null && pathStart.Length > 0)
            {
                sql.Append("(");
                for (int i = 0; i < pathStart.Length; i++)
                {
                    if (i > 0)
                        sql.Append(" OR ");
                    sql.Append($"Path LIKE @Path{i}");
                }
                sql.Append(")");
                first = false;
            }

            if (!string.IsNullOrEmpty(name))
            {
                if (!first)
                    sql.Append(" AND ");
                sql.Append("Name = @Name");
                first = false;
            }

            if (nodeTypeIds != null && nodeTypeIds.Length > 0)
            {
                if (!first)
                    sql.Append(" AND ");
                sql.Append("NodeTypeId IN (");
                sql.Append(string.Join(",", nodeTypeIds));
                sql.Append(")");
            }

            if (orderByPath)
                sql.Append(" ORDER BY Path");

            using var connection = new MySqlConnection(ConnectionStrings.Value.Repository);
            await connection.OpenAsync(cancellationToken);

            using var command = new MySqlCommand(sql.ToString(), connection);
            for (int i = 0; i < pathStart.Length; i++)
                command.Parameters.AddWithValue($"@Path{i}", $"{pathStart[i]}%");

            if (!string.IsNullOrEmpty(name))
                command.Parameters.AddWithValue("@Name", name);

            var result = new List<int>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(reader.GetInt32(0));
            }

            return result;
        }
    }
}