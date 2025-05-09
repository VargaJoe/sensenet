﻿using System;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.Configuration;
using SenseNet.Diagnostics;
using SenseNet.Tools;

// ReSharper disable once CheckNamespace
namespace SenseNet.ContentRepository.Storage.Data.MsSqlClient
{
    public class MsSqlDataContext : SnDataContext
    {
        public string ConnectionString { get; }

        public MsSqlDataContext(string connectionString, DataOptions options, IRetrier retrier, CancellationToken cancel)
            : base(options, retrier, cancel)
        {
            if (string.IsNullOrEmpty(connectionString))
                throw new ArgumentNullException(nameof(connectionString));
            
            ConnectionString = connectionString;
        }

        public static string GetConnectionString(ConnectionInfo connectionInfo, ConnectionStringOptions connectionStrings)
        {
            string cnstr;

            if (string.IsNullOrEmpty(connectionInfo.ConnectionName))
                cnstr = connectionStrings.Repository;
            else
                if (!connectionStrings.AllConnectionStrings.TryGetValue(connectionInfo.ConnectionName, out cnstr)
                    || cnstr == null)
                    throw new InvalidOperationException("Unknown connection name: " + connectionInfo.ConnectionName);

            var connectionBuilder = new SqlConnectionStringBuilder(cnstr);
            switch (connectionInfo.InitialCatalog)
            {
                case InitialCatalog.Initial:
                    break;
                case InitialCatalog.Master:
                    connectionBuilder.InitialCatalog = "master";
                    break;
                default:
                    throw new NotSupportedException("Unknown InitialCatalog");
            }

            if (!string.IsNullOrEmpty(connectionInfo.DataSource))
                connectionBuilder.DataSource = connectionInfo.DataSource;

            if (!string.IsNullOrEmpty(connectionInfo.InitialCatalogName)
                    && connectionInfo.InitialCatalog != InitialCatalog.Master)
                connectionBuilder.InitialCatalog = connectionInfo.InitialCatalogName;

            if (!string.IsNullOrWhiteSpace(connectionInfo.UserName))
            {
                if (string.IsNullOrWhiteSpace(connectionInfo.Password))
                    throw new NotSupportedException("Invalid credentials.");
                connectionBuilder.UserID = connectionInfo.UserName;
                connectionBuilder.Password = connectionInfo.Password;
                connectionBuilder.IntegratedSecurity = false;
            }
            else
            {
                connectionBuilder.Remove("User ID");
                connectionBuilder.Remove("Password");
                connectionBuilder.Remove("Persist Security Info");
                connectionBuilder.IntegratedSecurity = true;
            }
            return connectionBuilder.ToString();
        }

        public override DbConnection CreateConnection()
        {
            return CreateSqlConnection();
        }
        public override DbCommand CreateCommand()
        {
            return CreateSqlCommand();
        }
        public override DbParameter CreateParameter()
        {
            return CreateSqlParameter();
        }

        public SqlParameter CreateParameter(string name, SqlDbType dbType, object value)
        {
            return new SqlParameter
            {
                ParameterName = name,
                SqlDbType = dbType,
                Value = value
            };
        }
        public SqlParameter CreateParameter(string name, SqlDbType dbType, int size, object value)
        {
            return new SqlParameter
            {
                ParameterName = name,
                SqlDbType = dbType,
                Size = size,
                Value = value
            };
        }

        public virtual SqlConnection CreateSqlConnection()
        {
            return new SqlConnection(ConnectionString);
        }
        public virtual SqlCommand CreateSqlCommand()
        {
            return new SqlCommand();
        }
        public virtual SqlParameter CreateSqlParameter()
        {
            return new SqlParameter();
        }
        public override TransactionWrapper WrapTransaction(DbTransaction underlyingTransaction,
            CancellationToken cancellationToken, TimeSpan timeout = default(TimeSpan))
        {
            return null;
        }


        public async Task<int> ExecuteNonQueryAsync(string script, Action<SqlCommand> setParams = null)
        {
            using (var cmd = CreateSqlCommand())
            {
                SqlTransaction transaction = null;
                var cancellationToken = CancellationToken;
                if (Transaction != null)
                {
                    transaction = (SqlTransaction) Transaction.Transaction;
                    cancellationToken = Transaction.CancellationToken;
                }

                cmd.Connection = (SqlConnection) OpenConnection();
                cmd.CommandTimeout = DataOptions.DbCommandTimeout;
                cmd.CommandText = script;
                cmd.CommandType = CommandType.Text;
                cmd.Transaction = transaction;

                setParams?.Invoke(cmd);

                var result = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                return result;
            }
        }

        public async Task<object> ExecuteScalarAsync(string script, Action<SqlCommand> setParams = null)
        {
            using (var cmd = CreateSqlCommand())
            {
                SqlTransaction transaction = null;
                var cancellationToken = CancellationToken;
                if (Transaction != null)
                {
                    transaction = (SqlTransaction) Transaction.Transaction;
                    cancellationToken = Transaction.CancellationToken;
                }

                cmd.Connection = (SqlConnection) OpenConnection();
                cmd.CommandTimeout = DataOptions.DbCommandTimeout;
                cmd.CommandText = script;
                cmd.CommandType = CommandType.Text;
                cmd.Transaction = transaction;

                setParams?.Invoke(cmd);

                var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                return result;
            }
        }

        public Task<T> ExecuteReaderAsync<T>(string script, Func<SqlDataReader, CancellationToken, Task<T>> callback)
        {
            return ExecuteReaderAsync(script, null, callback);
        }

        public async Task<T> ExecuteReaderAsync<T>(string script, Action<SqlCommand> setParams,
            Func<SqlDataReader, CancellationToken, Task<T>> callbackAsync)
        {
            try
            {
                using (var cmd = CreateSqlCommand())
                {
                    SqlTransaction transaction = null;
                    var cancellationToken = CancellationToken;
                    if (Transaction != null)
                    {
                        transaction = (SqlTransaction) Transaction.Transaction;
                        cancellationToken = Transaction.CancellationToken;
                    }

                    cmd.Connection = (SqlConnection) OpenConnection();
                    cmd.CommandTimeout = DataOptions.DbCommandTimeout;
                    cmd.CommandText = script;
                    cmd.CommandType = CommandType.Text;
                    cmd.Transaction = transaction;

                    setParams?.Invoke(cmd);

                    using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var result = await callbackAsync(reader, cancellationToken).ConfigureAwait(false);
                        return result;
                    }
                }
            }
            catch (Exception e)
            {
                SnTrace.WriteError(e.ToString());
                throw;
            }
        }
    }
}
