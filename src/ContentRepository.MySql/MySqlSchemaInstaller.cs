using System;
using System.Collections.Generic;
using System.Data;
using MySql.Data.MySqlClient; // MySQL client library
using System.Linq;
using STT = System.Threading.Tasks;
using SenseNet.ContentRepository.Storage.DataModel;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    internal class MySqlSchemaInstaller
    {
        private static readonly byte Yes = 1;
        private static readonly byte No = 0;

        private static class TableName
        {
            public static readonly string PropertyTypes = "PropertyTypes";
            public static readonly string NodeTypes = "NodeTypes";
            public static readonly string ContentListTypes = "ContentListTypes";
        }

        private Dictionary<string, string[]> _columnNames;

        private readonly string _connectionString;

        public MySqlSchemaInstaller(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async STT.Task InstallSchemaAsync(RepositorySchemaData schema)
        {
            var dataSet = new DataSet();

            CreateTableStructure(dataSet);

            _columnNames = dataSet.Tables.Cast<DataTable>().ToDictionary(
                table => table.TableName,
                table => table.Columns.Cast<DataColumn>().Select(x => x.ColumnName).ToArray());

            CreateData(dataSet, schema);

            await WriteToDatabaseAsync(dataSet, _connectionString).ConfigureAwait(false);
        }

        /* ==================================================================================================== Tables */

        private void CreateTableStructure(DataSet dataSet)
        {
            AddNodeTypesTable(dataSet);
            AddPropertyTypesTable(dataSet);
            AddContentListTypesTable(dataSet);
        }

        private void AddPropertyTypesTable(DataSet dataSet)
        {
            var table = new DataTable(TableName.PropertyTypes);
            table.Columns.AddRange(new[]
            {
                new DataColumn {ColumnName = "PropertyTypeId", DataType = typeof(int), AllowDBNull = false},
                new DataColumn {ColumnName = "Name", DataType = typeof(string), AllowDBNull = false},
                new DataColumn {ColumnName = "DataType", DataType = typeof(string), AllowDBNull = false},
                new DataColumn {ColumnName = "Mapping", DataType = typeof(int), AllowDBNull = false},
                new DataColumn {ColumnName = "IsContentListProperty", DataType = typeof(byte), AllowDBNull = false},
            });
            dataSet.Tables.Add(table);
        }

        private void AddNodeTypesTable(DataSet dataSet)
        {
            var table = new DataTable(TableName.NodeTypes);
            table.Columns.AddRange(new[]
            {
                new DataColumn {ColumnName = "NodeTypeId", DataType = typeof(int), AllowDBNull = false},
                new DataColumn {ColumnName = "ParentId", DataType = typeof(int), AllowDBNull = true},
                new DataColumn {ColumnName = "Name", DataType = typeof(string), AllowDBNull = false},
                new DataColumn {ColumnName = "ClassName", DataType = typeof(string), AllowDBNull = false},
                new DataColumn {ColumnName = "Properties", DataType = typeof(string), AllowDBNull = false},
            });
            dataSet.Tables.Add(table);
        }

        private void AddContentListTypesTable(DataSet dataSet)
        {
            var table = new DataTable(TableName.ContentListTypes);
            table.Columns.AddRange(new[]
            {
                new DataColumn {ColumnName = "ContentListTypeId", DataType = typeof(int), AllowDBNull = false},
                new DataColumn {ColumnName = "Name", DataType = typeof(string), AllowDBNull = false},
                new DataColumn {ColumnName = "Properties", DataType = typeof(string), AllowDBNull = false},
            });
            dataSet.Tables.Add(table);
        }

        /* ==================================================================================================== Fill Data */

        private void CreateData(DataSet dataSet, RepositorySchemaData schema)
        {
            var propertyTypes = dataSet.Tables[TableName.PropertyTypes];
            foreach (var propertyType in schema.PropertyTypes)
            {
                var row = propertyTypes.NewRow();
                SetPropertyTypeRow(row, propertyType);
                propertyTypes.Rows.Add(row);
            }

            var nodeTypes = dataSet.Tables[TableName.NodeTypes];
            foreach (var nodeType in schema.NodeTypes)
            {
                var row = nodeTypes.NewRow();
                SetNodeTypeRow(row, nodeType, schema.NodeTypes);
                nodeTypes.Rows.Add(row);
            }

            var contentListTypes = dataSet.Tables[TableName.ContentListTypes];
            foreach (var contentListType in schema.ContentListTypes)
            {
                var row = contentListTypes.NewRow();
                SetContentListTypeRow(row, contentListType);
                contentListTypes.Rows.Add(row);
            }
        }

        private void SetPropertyTypeRow(DataRow row, PropertyTypeData propertyType)
        {
            row["PropertyTypeId"] = propertyType.Id;
            row["Name"] = propertyType.Name;
            row["DataType"] = propertyType.DataType.ToString();
            row["Mapping"] = propertyType.Mapping;
            row["IsContentListProperty"] = propertyType.IsContentListProperty ? Yes : No;
        }

        private void SetNodeTypeRow(DataRow row, NodeTypeData nodeType, List<NodeTypeData> allNodeTypes)
        {
            row["NodeTypeId"] = nodeType.Id;
            row["Name"] = nodeType.Name;
            row["ParentId"] = (object)allNodeTypes.FirstOrDefault(x => x.Name == nodeType.ParentName)?.Id ??
                              DBNull.Value;
            row["ClassName"] = nodeType.ClassName;
            row["Properties"] = string.Join(" ", nodeType.Properties);
        }

        private void SetContentListTypeRow(DataRow row, ContentListTypeData contentListType)
        {
            row["ContentListTypeId"] = contentListType.Id;
            row["Name"] = contentListType.Name;
            row["Properties"] = string.Join(" ", contentListType.Properties);
        }

        /* ==================================================================================================== Writing */

        private async STT.Task WriteToDatabaseAsync(DataSet dataSet, string connectionString)
        {
            using (var connection = new MySqlConnection(connectionString))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                using (var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false))
                {
                    await BulkInsertAsync(dataSet, TableName.PropertyTypes, connection, transaction).ConfigureAwait(false);
                    await BulkInsertAsync(dataSet, TableName.NodeTypes, connection, transaction).ConfigureAwait(false);
                    await BulkInsertAsync(dataSet, TableName.ContentListTypes, connection, transaction).ConfigureAwait(false);
                    await transaction.CommitAsync().ConfigureAwait(false);
                }
            }
        }

        private async STT.Task BulkInsertAsync(DataSet dataSet, string tableName, MySqlConnection connection, MySqlTransaction transaction)
        {
            var table = dataSet.Tables[tableName];
            if (table.Rows.Count == 0)
                return;

            // Truncate the table to remove old data
            using (var command = new MySqlCommand($"TRUNCATE TABLE `{tableName}`", connection, transaction))
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            // Insert data in batches
            var columns = string.Join(", ", table.Columns.Cast<DataColumn>().Select(c => $"`{c.ColumnName}`"));
            var values = string.Join(", ", table.Columns.Cast<DataColumn>().Select(c => $"@{c.ColumnName}"));

            var insertCommandText = $"INSERT INTO `{tableName}` ({columns}) VALUES ({values})";

            foreach (DataRow row in table.Rows)
            {
                using (var command = new MySqlCommand(insertCommandText, connection, transaction))
                {
                    foreach (DataColumn column in table.Columns)
                    {
                        command.Parameters.AddWithValue($"@{column.ColumnName}", row[column] ?? DBNull.Value);
                    }

                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }
    }
}