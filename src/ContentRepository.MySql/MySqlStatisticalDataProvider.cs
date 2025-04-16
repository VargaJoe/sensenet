using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.Diagnostics;
using SenseNet.Tools;

namespace SenseNet.Storage.Data.MySqlClient
{
    public class MySqlStatisticalDataProvider : IStatisticalDataProvider
    {
        private readonly IRetrier _retrier;
        protected DataOptions DataOptions { get; }
        private string ConnectionString { get; }

        public MySqlStatisticalDataProvider(IOptions<DataOptions> options, IOptions<ConnectionStringOptions> connectionOptions, IRetrier retrier)
        {
            _retrier = retrier;
            DataOptions = options?.Value ?? new DataOptions();

            if (connectionOptions == null)
                throw new ArgumentNullException(nameof(connectionOptions));
            ConnectionString = connectionOptions.Value.Repository;
            if (ConnectionString == null)
                throw new ArgumentException($"The {connectionOptions.Value.Repository} cannot be null");
        }

        private static readonly string WriteDataScript = @"-- MySqlStatisticalDataProvider.WriteData
INSERT INTO StatisticalData
    (DataType, CreationTime, WrittenTime, Duration, RequestLength, ResponseLength, ResponseStatusCode, `Url`,
     TargetId, ContentId, EventName, ErrorMessage, GeneralData)
    VALUES
    (@DataType, @CreationTime, @WrittenTime, @Duration, @RequestLength, @ResponseLength, @ResponseStatusCode, @Url,
     @TargetId, @ContentId, @EventName, @ErrorMessage, @GeneralData)";
        
        public async Task WriteDataAsync(IStatisticalDataRecord data, CancellationToken cancellation)
        {
            using var op = SnTrace.Database.StartOperation("MySqlStatisticalDataProvider: " +
                "WriteData: DataType: {0}", data.DataType);
            using var ctx = new MySqlDataContext(ConnectionString, DataOptions, _retrier, cancellation);
            await ctx.ExecuteNonQueryAsync(WriteDataScript, cmd =>
            {
                var now = DateTime.UtcNow;
                cmd.Parameters.AddRange(new[]
                {
                    ctx.CreateParameter("@DataType", DbType.String, data.DataType),
                    ctx.CreateParameter("@WrittenTime", DbType.DateTime, now),
                    ctx.CreateParameter("@CreationTime", DbType.DateTime, data.CreationTime ?? now),
                    ctx.CreateParameter("@Duration", DbType.Int64, (object)data.Duration?.Ticks ?? DBNull.Value),
                    ctx.CreateParameter("@RequestLength", DbType.Int64, (object)data.RequestLength ?? DBNull.Value),
                    ctx.CreateParameter("@ResponseLength", DbType.Int64, (object)data.ResponseLength ?? DBNull.Value),
                    ctx.CreateParameter("@ResponseStatusCode", DbType.Int32, (object)data.ResponseStatusCode ?? DBNull.Value),
                    ctx.CreateParameter("@Url", DbType.String, (object)data.Url ?? DBNull.Value),
                    ctx.CreateParameter("@TargetId", DbType.Int32, (object)data.TargetId ?? DBNull.Value),
                    ctx.CreateParameter("@ContentId", DbType.Int32, (object)data.ContentId ?? DBNull.Value),
                    ctx.CreateParameter("@EventName", DbType.String, (object)data.EventName ?? DBNull.Value),
                    ctx.CreateParameter("@ErrorMessage", DbType.String, (object)data.ErrorMessage ?? DBNull.Value),
                    ctx.CreateParameter("@GeneralData", DbType.String, (object)data.GeneralData ?? DBNull.Value),
                });
            }).ConfigureAwait(false);
            op.Successful = true;
        }

        private static readonly string LoadUsageListScript = @"-- MySqlStatisticalDataProvider.LoadUsageList
SELECT * FROM StatisticalData
WHERE DataType = @DataType AND CreationTime < @EndTimeExclusive
ORDER BY CreationTime DESC
LIMIT @Take";

        private static readonly string LoadUsageListByTargetIdsScript = @"-- MySqlStatisticalDataProvider.LoadUsageListByTargetIds
SELECT * FROM StatisticalData
WHERE DataType = @DataType AND CreationTime < @EndTimeExclusive AND TargetId IN ({0})
ORDER BY CreationTime DESC
LIMIT @Take";

        public async Task<IEnumerable<IStatisticalDataRecord>> LoadUsageListAsync(string dataType, int[] relatedTargetIds, DateTime endTimeExclusive, int count, CancellationToken cancellation)
        {
            using var op = SnTrace.Database.StartOperation(() => "MySqlStatisticalDataProvider: " +
                $"LoadUsageList(dataType: {dataType}, relatedTargetIds: {relatedTargetIds?.ToTrace()}, " +
                $"endTimeExclusive: {endTimeExclusive:yyyy-MM-dd HH:mm:ss.fffff}, count: {count})");

            var sql = relatedTargetIds == null || relatedTargetIds.Length == 0
                ? LoadUsageListScript
                : string.Format(LoadUsageListByTargetIdsScript,
                    string.Join(", ", relatedTargetIds.Select(x => x.ToString())));
            using var ctx = new MySqlDataContext(ConnectionString, DataOptions, _retrier, cancellation);
            var records = new List<IStatisticalDataRecord>();
            await ctx.ExecuteReaderAsync(sql, cmd =>
            {
                cmd.Parameters.AddRange(new[]
                {
                    ctx.CreateParameter("@Take", DbType.Int32, count),
                    ctx.CreateParameter("@DataType", DbType.String, dataType),
                    ctx.CreateParameter("@EndTimeExclusive", DbType.DateTime, endTimeExclusive),
                });
            }, async (reader, cancel) =>
            {
                while (await reader.ReadAsync(cancel))
                    records.Add(GetStatisticalDataRecordFromReader(reader));
                return true;
            }).ConfigureAwait(false);
            op.Successful = true;

            return records;
        }

        /* Similar changes will be made to other methods and SQL scripts to ensure compatibility with MySQL,
           including adapting table creation scripts, parameter types, and handling of upsert operations. */

        private IStatisticalDataRecord GetStatisticalDataRecordFromReader(DbDataReader reader)
        {
            var durationIndex = reader.GetOrdinal("Duration");
            return new StatisticalDataRecord
            {
                Id = reader.GetSafeInt32(reader.GetOrdinal("Id")),
                DataType = reader.GetSafeString(reader.GetOrdinal("DataType")),
                WrittenTime = reader.GetDateTimeUtc(reader.GetOrdinal("WrittenTime")),
                CreationTime = reader.GetDateTimeUtc(reader.GetOrdinal("CreationTime")),
                Duration = reader.IsDBNull(durationIndex) ? (TimeSpan?)null : TimeSpan.FromTicks(reader.GetInt64(durationIndex)),
                RequestLength = reader.GetLongOrNull("RequestLength"),
                ResponseLength = reader.GetLongOrNull("ResponseLength"),
                ResponseStatusCode = reader.GetIntOrNull("ResponseStatusCode"),
                Url = reader.GetStringOrNull("Url"),
                TargetId = reader.GetIntOrNull("TargetId"),
                ContentId = reader.GetIntOrNull("ContentId"),
                EventName = reader.GetStringOrNull("EventName"),
                ErrorMessage = reader.GetStringOrNull("ErrorMessage"),
                GeneralData = reader.GetStringOrNull("GeneralData"),
            };
        }
    }
}