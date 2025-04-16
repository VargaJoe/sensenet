using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using STT = System.Threading.Tasks;
using SenseNet.Configuration;
using SenseNet.Diagnostics;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    /// <summary> 
    /// This is a MySQL implementation of the <see cref="IExclusiveLockDataProvider"/> interface.
    /// It requires the main data provider to be a <see cref="RelationalDataProviderBase"/>.
    /// </summary>
    public class MySqlExclusiveLockDataProvider : IExclusiveLockDataProvider
    {
        private const string AcquireScript = @"
-- MySqlExclusiveLockDataProvider.Acquire
INSERT INTO ExclusiveLocks (Name) 
VALUES (@Name)
ON DUPLICATE KEY UPDATE OperationId = @OperationId, TimeLimit = @TimeLimit;

SELECT Id FROM ExclusiveLocks
WHERE Name = @Name AND (OperationId IS NULL OR TimeLimit <= NOW())
LIMIT 1;
";

        private const string RefreshScript = @"
UPDATE ExclusiveLocks
SET TimeLimit = @TimeLimit
WHERE Name = @Name;
";

        private const string ReleaseScript = @"
UPDATE ExclusiveLocks
SET OperationId = NULL
WHERE Name = @Name;
";

        private const string IsLockedScript = @"
SELECT Id FROM ExclusiveLocks
WHERE Name = @Name AND OperationId IS NOT NULL AND TimeLimit > NOW();
";

        private RelationalDataProviderBase _dataProvider;
        private RelationalDataProviderBase MainProvider =>
            _dataProvider ??= (_dataProvider = (RelationalDataProviderBase)Providers.Instance.DataProvider);

        /// <inheritdoc/>
        public async STT.Task<bool> AcquireAsync(string key, string operationId, DateTime timeLimit,
            CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation("MySqlExclusiveLockDataProvider: " +
                "Acquire(key: {0}, operationId: {1}, timeLimit: {2:yyyy-MM-dd HH:mm:ss.fffff})", key, operationId, timeLimit);

            using var ctx = MainProvider.CreateDataContext(cancellationToken);
            var result = await ctx.ExecuteScalarAsync(AcquireScript, cmd =>
            {
                cmd.Parameters.AddRange(new[]
                {
                    ctx.CreateParameter("@Name", DbType.String, key),
                    ctx.CreateParameter("@OperationId", DbType.String, operationId),
                    ctx.CreateParameter("@TimeLimit", DbType.DateTime, timeLimit)
                });
            }).ConfigureAwait(false);

            SnTrace.Database.Write($"MySqlExclusiveLockDataProvider: Acquire result: {{0}}", result == null ? "[null]" : "ACQUIRED " + result);
            op.Successful = true;

            return result != DBNull.Value && result != null;
        }

        /// <inheritdoc/>
        public async STT.Task RefreshAsync(string key, string operationId, DateTime newTimeLimit,
            CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation("MySqlExclusiveLockDataProvider: " +
                "Refresh(key: {0}, operationId: {1}, newTimeLimit: {2:yyyy-MM-dd HH:mm:ss.fffff})",
                key, operationId, newTimeLimit);

            using var ctx = MainProvider.CreateDataContext(cancellationToken);
            await ctx.ExecuteNonQueryAsync(RefreshScript,
                cmd =>
                {
                    cmd.Parameters.AddRange(new[]
                    {
                        ctx.CreateParameter("@Name", DbType.String, key),
                        ctx.CreateParameter("@TimeLimit", DbType.DateTime, newTimeLimit)
                    });
                }).ConfigureAwait(false);

            op.Successful = true;
        }

        /// <inheritdoc/>
        public async STT.Task ReleaseAsync(string key, string operationId, CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation("MySqlExclusiveLockDataProvider: " +
                "Release(key: {0}, operationId: {1})", key, operationId);

            using var ctx = MainProvider.CreateDataContext(cancellationToken);
            await ctx.ExecuteNonQueryAsync(ReleaseScript, cmd =>
            {
                cmd.Parameters.AddRange(new[]
                {
                    ctx.CreateParameter("@Name", DbType.String, key)
                });
            }).ConfigureAwait(false);

            op.Successful = true;
        }

        /// <inheritdoc/>
        public async STT.Task<bool> IsLockedAsync(string key, string operationId, CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation("MySqlExclusiveLockDataProvider: " +
                "IsLocked(key: {0}, operationId: {1})", key, operationId);

            using var ctx = MainProvider.CreateDataContext(cancellationToken);
            var result = await ctx.ExecuteScalarAsync(IsLockedScript,
                cmd =>
                {
                    cmd.Parameters.AddRange(new[]
                    {
                        ctx.CreateParameter("@Name", DbType.String, key)
                    });
                }).ConfigureAwait(false);

            SnTrace.Database.Write("MySqlExclusiveLockDataProvider: IsLocked result: {0}",
                result == null || result == DBNull.Value ? "[null]" : "LOCKED");
            op.Successful = true;

            return result != DBNull.Value && result != null;
        }

        /// <inheritdoc/>
        public async STT.Task<bool> IsFeatureAvailable(CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation("MySqlExclusiveLockDataProvider: IsFeatureAvailable()");
            using var ctx = MainProvider.CreateDataContext(cancellationToken);

            var result = await ctx.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'ExclusiveLocks'")
                .ConfigureAwait(false);

            op.Successful = true;
            return result != DBNull.Value && Convert.ToInt32(result) > 0;
        }

        /// <inheritdoc/>
        public async STT.Task ReleaseAllAsync(CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation("MySqlExclusiveLockDataProvider: ReleaseAll()");
            using var ctx = MainProvider.CreateDataContext(cancellationToken);
            await ctx.ExecuteNonQueryAsync("DELETE FROM ExclusiveLocks").ConfigureAwait(false);
            op.Successful = true;
        }

        /* ====================================================================================== INSTALLATION SCRIPTS */

        public static readonly string DropScript = @"
DROP TABLE IF EXISTS `ExclusiveLocks`;
";

        public static readonly string CreationScript = @"
CREATE TABLE IF NOT EXISTS `ExclusiveLocks` (
    `Id` INT AUTO_INCREMENT PRIMARY KEY,
    `Name` VARCHAR(450) NOT NULL UNIQUE,
    `OperationId` VARCHAR(450) NULL,
    `TimeLimit` DATETIME NULL,
    UNIQUE INDEX `IX_ExclusiveLock_Name` (`Name`),
    INDEX `IX_ExclusiveLock_Name_TimeLimit` (`Name`, `TimeLimit`)
) ENGINE=InnoDB;
";
    }
}