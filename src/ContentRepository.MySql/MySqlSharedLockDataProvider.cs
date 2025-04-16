using System;
using System.Data;
using System.Threading;
using STT = System.Threading.Tasks;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.Diagnostics;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    /// <summary> 
    /// This is a MySQL implementation of the <see cref="ISharedLockDataProvider"/> interface.
    /// It requires the main data provider to be a <see cref="RelationalDataProviderBase"/>.
    /// </summary>
    public class MySqlSharedLockDataProvider : ISharedLockDataProvider
    {
        public TimeSpan SharedLockTimeout { get; } = TimeSpan.FromMinutes(30d);

        private readonly RelationalDataProviderBase _mainProvider;

        public MySqlSharedLockDataProvider(DataProvider mainProvider)
        {
            if (mainProvider == null)
                return;
            if (!(mainProvider is RelationalDataProviderBase relationalDataProviderBase))
                throw new ArgumentException("The mainProvider needs to be RelationalDataProviderBase.");
            _mainProvider = relationalDataProviderBase;
        }

        public async STT.Task DeleteAllSharedLocksAsync(CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation("MySqlSharedLockDataProvider: DeleteAllSharedLocks()");
            using var ctx = _mainProvider.CreateDataContext(cancellationToken);
            await ctx.ExecuteNonQueryAsync("TRUNCATE TABLE `SharedLocks`").ConfigureAwait(false);
            op.Successful = true;
        }

        public async STT.Task CreateSharedLockAsync(int contentId, string @lock, CancellationToken cancellationToken)
        {
            var timeLimit = DateTime.UtcNow.AddTicks(-SharedLockTimeout.Ticks);
            const string sql = @"
DELETE FROM `SharedLocks` WHERE `ContentId` = @ContentId AND `CreationDate` < @TimeLimit;
SELECT `Lock` FROM `SharedLocks` WHERE `ContentId` = @ContentId INTO @Result;

IF @Result IS NULL THEN
    INSERT INTO `SharedLocks` (`ContentId`, `Lock`, `CreationDate`)
    VALUES (@ContentId, @Lock, UTC_TIMESTAMP());
    SET @Result = NULL;
ELSEIF @Result = @Lock THEN
    UPDATE `SharedLocks` SET `CreationDate` = UTC_TIMESTAMP() WHERE `ContentId` = @ContentId;
    SET @Result = NULL;
END IF;

SELECT @Result;
";

            using var op = SnTrace.Database.StartOperation("MySqlSharedLockDataProvider: CreateSharedLock(contentId: {0}, lock: {1})", contentId, @lock);
            using var ctx = _mainProvider.CreateDataContext(cancellationToken);
            var result = await ctx.ExecuteScalarAsync(sql, cmd =>
            {
                cmd.Parameters.AddRange(new[]
                {
                    ctx.CreateParameter("@ContentId", DbType.Int32, contentId),
                    ctx.CreateParameter("@Lock", DbType.String, @lock),
                    ctx.CreateParameter("@TimeLimit", DbType.DateTime, timeLimit)
                });
            }).ConfigureAwait(false);

            var existingLock = result == DBNull.Value ? null : (string)result;
            if (existingLock != null)
                throw new LockedNodeException(null, $"The node (#{contentId}) is locked by another shared lock.");

            op.Successful = true;
        }

        public async STT.Task<string> RefreshSharedLockAsync(int contentId, string @lock, CancellationToken cancellationToken)
        {
            var timeLimit = DateTime.UtcNow.AddTicks(-SharedLockTimeout.Ticks);
            const string sql = @"
DELETE FROM `SharedLocks` WHERE `ContentId` = @ContentId AND `CreationDate` < @TimeLimit;
SELECT `Lock` FROM `SharedLocks` WHERE `ContentId` = @ContentId INTO @Result;

IF @Result = @Lock THEN
    UPDATE `SharedLocks` SET `CreationDate` = UTC_TIMESTAMP() WHERE `ContentId` = @ContentId;
END IF;

SELECT @Result;
";

            using var op = SnTrace.Database.StartOperation("MySqlSharedLockDataProvider: RefreshSharedLock(contentId: {0}, lock: {1})", contentId, @lock);
            using var ctx = _mainProvider.CreateDataContext(cancellationToken);
            var result = await ctx.ExecuteScalarAsync(sql, cmd =>
            {
                cmd.Parameters.AddRange(new[]
                {
                    ctx.CreateParameter("@ContentId", DbType.Int32, contentId),
                    ctx.CreateParameter("@Lock", DbType.String, @lock),
                    ctx.CreateParameter("@TimeLimit", DbType.DateTime, timeLimit)
                });
            }).ConfigureAwait(false);

            var existingLock = result == DBNull.Value ? null : (string)result;
            if (existingLock == null)
                throw new SharedLockNotFoundException("Content is unlocked");
            if (existingLock != @lock)
                throw new LockedNodeException(null, $"The node (#{contentId}) is locked by another shared lock.");
            op.Successful = true;

            return existingLock;
        }

        public async STT.Task<string> GetSharedLockAsync(int contentId, CancellationToken cancellationToken)
        {
            var timeLimit = DateTime.UtcNow.AddTicks(-SharedLockTimeout.Ticks);
            const string sql = "SELECT `Lock` FROM `SharedLocks` WHERE `ContentId` = @ContentId AND `CreationDate` >= @TimeLimit";

            using var op = SnTrace.Database.StartOperation("MySqlSharedLockDataProvider: GetSharedLock(contentId: {0})", contentId);
            using var ctx = _mainProvider.CreateDataContext(cancellationToken);
            var result = await ctx.ExecuteScalarAsync(sql, cmd =>
            {
                cmd.Parameters.AddRange(new[]
                {
                    ctx.CreateParameter("@ContentId", DbType.Int32, contentId),
                    ctx.CreateParameter("@TimeLimit", DbType.DateTime, timeLimit)
                });
            }).ConfigureAwait(false);

            op.Successful = true;
            return result == DBNull.Value ? null : (string)result;
        }
    }
}