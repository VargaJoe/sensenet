using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using STT = System.Threading.Tasks;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.Diagnostics;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    /// <summary>
    /// This is a MySQL implementation of the <see cref="IAccessTokenDataProvider"/> interface.
    /// It requires the main data provider to be a <see cref="RelationalDataProviderBase"/>.
    /// </summary>
    public class MySqlAccessTokenDataProvider : IAccessTokenDataProvider
    {
        private readonly RelationalDataProviderBase _mainProvider;

        public MySqlAccessTokenDataProvider(DataProvider mainProvider)
        {
            if (mainProvider == null)
                return;
            if (!(mainProvider is RelationalDataProviderBase relationalDataProviderBase))
                throw new ArgumentException("The mainProvider needs to be RelationalDataProviderBase.");
            _mainProvider = relationalDataProviderBase;
        }

        private async STT.Task<string> GetAccessTokenValueCollationNameAsync(CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT COLLATION_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'AccessTokens' AND COLUMN_NAME = 'Value'";

            using var op = SnTrace.Database.StartOperation(
                "MySqlAccessTokenDataProvider: GetAccessTokenValueCollationName()");
            using var ctx = _mainProvider.CreateDataContext(cancellationToken);
            var result = await ctx.ExecuteScalarAsync(sql).ConfigureAwait(false);
            var originalCollation = Convert.ToString(result);
            op.Successful = true;
            return originalCollation.Replace("_ci", "_cs");
        }

        private static AccessToken GetAccessTokenFromReader(IDataReader reader)
        {
            return new AccessToken
            {
                Id = reader.GetInt32(reader.GetOrdinal("AccessTokenId")),
                Value = reader.GetString(reader.GetOrdinal("Value")),
                UserId = reader.GetInt32(reader.GetOrdinal("UserId")),
                ContentId = reader.GetSafeInt32(reader.GetOrdinal("ContentId")),
                Feature = reader.GetSafeString(reader.GetOrdinal("Feature")),
                CreationDate = reader.GetDateTime(reader.GetOrdinal("CreationDate")),
                ExpirationDate = reader.GetDateTime(reader.GetOrdinal("ExpirationDate")),
            };
        }

        public async STT.Task DeleteAllAccessTokensAsync(CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation("MySqlAccessTokenDataProvider: DeleteAllAccessTokens()");
            using var ctx = _mainProvider.CreateDataContext(cancellationToken);
            await ctx.ExecuteNonQueryAsync("TRUNCATE TABLE AccessTokens").ConfigureAwait(false);
            op.Successful = true;
        }

        public async STT.Task SaveAccessTokenAsync(AccessToken token, CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO AccessTokens
                (Value, UserId, ContentId, Feature, CreationDate, ExpirationDate)
                VALUES
                (@Value, @UserId, @ContentId, @Feature, @CreationDate, @ExpirationDate);
                SELECT LAST_INSERT_ID();";

            using var op = SnTrace.Database.StartOperation("MySqlAccessTokenDataProvider: SaveAccessToken");
            using var ctx = _mainProvider.CreateDataContext(cancellationToken);
            var result = await ctx.ExecuteScalarAsync(sql, cmd =>
            {
                cmd.Parameters.AddRange(new[]
                {
                    ctx.CreateParameter("@Value", DbType.String, token.Value),
                    ctx.CreateParameter("@UserId", DbType.Int32, token.UserId),
                    ctx.CreateParameter("@ContentId", DbType.Int32, token.ContentId != 0 ? (object)token.ContentId : DBNull.Value),
                    ctx.CreateParameter("@Feature", DbType.String, token.Feature ?? DBNull.Value),
                    ctx.CreateParameter("@CreationDate", DbType.DateTime, token.CreationDate),
                    ctx.CreateParameter("@ExpirationDate", DbType.DateTime, token.ExpirationDate)
                });
            }).ConfigureAwait(false);
            token.Id = Convert.ToInt32(result);
            op.Successful = true;
        }

        // Other methods (LoadAccessTokenByIdAsync, LoadAccessTokenAsync, etc.) follow a similar pattern
        // and should be adapted for MySQL syntax as shown above.
    }
}