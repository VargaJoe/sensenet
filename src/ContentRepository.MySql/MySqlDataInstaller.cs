using System;
using System.Collections.Generic;
using System.Data;
using MySql.Data.MySqlClient; // MySQL client library
using System.Linq;
using System.Threading;
using STT = System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Storage.DataModel;
using SenseNet.ContentRepository.Storage.Schema;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    public class MySqlDataInstaller : IDataInstaller
    {
        private static readonly byte Yes = 1;
        private static readonly byte No = 0;

        private static class TableName
        {
            public static readonly string PropertyTypes = "PropertyTypes";
            public static readonly string NodeTypes = "NodeTypes";
            public static readonly string Nodes = "Nodes";
            public static readonly string Versions = "Versions";
            public static readonly string LongTextProperties = "LongTextProperties";
            public static readonly string ReferenceProperties = "ReferenceProperties";
            public static readonly string BinaryProperties = "BinaryProperties";
            public static readonly string Files = "Files";
        }

        private Dictionary<string, string[]> _columnNames;
        private ILogger _logger;
        private ConnectionStringOptions ConnectionStrings { get; }

        public MySqlDataInstaller(IOptions<ConnectionStringOptions> connectionOptions,
            ILogger<MySqlDataInstaller> logger)
        {
            _columnNames = new Dictionary<string, string[]>();
            ConnectionStrings = connectionOptions?.Value ?? new ConnectionStringOptions();
            _logger = logger;
        }

        public async STT.Task InstallInitialDataAsync(InitialData data, DataProvider dataProvider, CancellationToken cancel)
        {
            if (dataProvider is not MySqlDataProvider mySqlDataProvider)
                throw new InvalidOperationException("MySqlDataInstaller error: Data provider is expected to be MySqlDataProvider.");

            var dataSet = new DataSet();

            CreateTableStructure(dataSet);

            _columnNames = dataSet.Tables.Cast<DataTable>().ToDictionary(
                table => table.TableName,
                table => table.Columns.Cast<DataColumn>().Select(x => x.ColumnName).ToArray());

            CreateData(dataSet, data, mySqlDataProvider);

            await WriteToDatabaseAsync(dataSet, ConnectionStrings.Repository, cancel).ConfigureAwait(false);
        }

        /* ==================================================================================================== Tables */

        private static void CreateTableStructure(DataSet dataSet)
        {
            AddNodeTypesTable(dataSet);
            AddPropertyTypesTable(dataSet);
            AddNodesTable(dataSet);
            AddVersionsTable(dataSet);
            AddLongTextPropertiesTable(dataSet);
            AddReferencePropertiesTable(dataSet);
            AddBinaryPropertiesTable(dataSet);
            AddFilesTable(dataSet);
        }

        private static void AddPropertyTypesTable(DataSet dataSet)
        {
            var table = new DataTable(TableName.PropertyTypes);
            table.Columns.AddRange(new[]
            {
                new DataColumn {ColumnName = "PropertyTypeId", DataType = typeof(int), AllowDBNull = false },
                new DataColumn {ColumnName = "Name", DataType = typeof(string), AllowDBNull = false },
                new DataColumn {ColumnName = "DataType", DataType = typeof(string), AllowDBNull = false },
                new DataColumn {ColumnName = "Mapping", DataType = typeof(int), AllowDBNull = false },
                new DataColumn {ColumnName = "IsContentListProperty", DataType = typeof(byte), AllowDBNull = false},
            });
            dataSet.Tables.Add(table);
        }

        private static void AddNodeTypesTable(DataSet dataSet)
        {
            var table = new DataTable(TableName.NodeTypes);
            table.Columns.AddRange(new[]
            {
                new DataColumn {ColumnName = "NodeTypeId", DataType = typeof(int), AllowDBNull = false },
                new DataColumn {ColumnName = "ParentId", DataType = typeof(int), AllowDBNull = true },
                new DataColumn {ColumnName = "Name", DataType = typeof(string), AllowDBNull = false },
                new DataColumn {ColumnName = "ClassName", DataType = typeof(string), AllowDBNull = false },
                new DataColumn {ColumnName = "Properties", DataType = typeof(string), AllowDBNull = false},
            });
            dataSet.Tables.Add(table);
        }

        private static void AddNodesTable(DataSet dataSet)
        {
            var table = new DataTable(TableName.Nodes);
            table.Columns.AddRange(new[]
            {
                new DataColumn {ColumnName = "NodeId", DataType = typeof(int)},
                new DataColumn {ColumnName = "NodeTypeId", DataType = typeof(int)},
                new DataColumn {ColumnName = "CreatingInProgress", DataType = typeof(byte), AllowDBNull = false },
                new DataColumn {ColumnName = "IsDeleted", DataType = typeof(byte), AllowDBNull = false},
                new DataColumn {ColumnName = "IsInherited", DataType = typeof(byte), AllowDBNull = false},
                new DataColumn {ColumnName = "ParentNodeId", DataType = typeof(int)},
                new DataColumn {ColumnName = "Name", DataType = typeof(string)},
                new DataColumn {ColumnName = "Path", DataType = typeof(string)},
                new DataColumn {ColumnName = "Index", DataType = typeof(int)},
                new DataColumn {ColumnName = "Locked", DataType = typeof(byte), AllowDBNull = false},
                new DataColumn {ColumnName = "ETag", DataType = typeof(string)},
                new DataColumn {ColumnName = "LockType", DataType = typeof(int)},
                new DataColumn {ColumnName = "LockTimeout", DataType = typeof(int)},
                new DataColumn {ColumnName = "LockDate", DataType = typeof(DateTime)},
                new DataColumn {ColumnName = "LockToken", DataType = typeof(string)},
                new DataColumn {ColumnName = "LastLockUpdate", DataType = typeof(DateTime)},
                new DataColumn {ColumnName = "LastMinorVersionId", DataType = typeof(int)},
                new DataColumn {ColumnName = "LastMajorVersionId", DataType = typeof(int)},
                new DataColumn {ColumnName = "CreationDate", DataType = typeof(DateTime)},
                new DataColumn {ColumnName = "CreatedById", DataType = typeof(int)},
                new DataColumn {ColumnName = "ModificationDate", DataType = typeof(DateTime)},
                new DataColumn {ColumnName = "ModifiedById", DataType = typeof(int)},
                new DataColumn {ColumnName = "IsSystem", DataType = typeof(byte), AllowDBNull = true},
                new DataColumn {ColumnName = "OwnerId", DataType = typeof(int)},
                new DataColumn {ColumnName = "SavingState", DataType = typeof(int)},
            });
            dataSet.Tables.Add(table);
        }

        // Other Add*Table methods would follow the same pattern as above.

        /* ==================================================================================================== Fill Data */

        private static void CreateData(DataSet dataSet, InitialData data, MySqlDataProvider dataProvider)
        {
            var now = DateTime.UtcNow;

            var propertyTypes = dataSet.Tables[TableName.PropertyTypes];
            foreach (var propertyType in data.Schema.PropertyTypes)
            {
                var row = propertyTypes.NewRow();
                SetPropertyTypeRow(row, propertyType, dataProvider);
                propertyTypes.Rows.Add(row);
            }

            // Similar logic applies for filling NodeTypes, Nodes, Versions, etc.
        }

        private static void SetPropertyTypeRow(DataRow row, PropertyTypeData propertyType, MySqlDataProvider dataProvider)
        {
            row["PropertyTypeId"] = propertyType.Id;
            row["Name"] = propertyType.Name;
            row["DataType"] = propertyType.DataType.ToString();
            row["Mapping"] = propertyType.Mapping;
            row["IsContentListProperty"] = propertyType.IsContentListProperty ? Yes : No;
        }

        /* ==================================================================================================== Writing */

        private async STT.Task WriteToDatabaseAsync(DataSet dataSet, string connectionString, CancellationToken cancellationToken)
        {
            foreach (var tableName in _columnNames.Keys)
            {
                await BulkInsertAsync(dataSet, tableName, connectionString, cancellationToken).ConfigureAwait(false);
            }
        }

        private async STT.Task BulkInsertAsync(DataSet dataSet, string tableName, string connectionString, CancellationToken cancellationToken)
        {
            _logger.LogTrace($"BulkInsert: inserting into table {tableName}");

            using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            var table = dataSet.Tables[tableName];

            foreach (DataRow row in table.Rows)
            {
                // Construct and execute an INSERT statement for each row
                // Example:
                // INSERT INTO `tableName` (columns...) VALUES (values...);
            }
        }
    }
}