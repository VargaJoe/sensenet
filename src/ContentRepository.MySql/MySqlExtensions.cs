using System;
using Microsoft.Extensions.DependencyInjection;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Components;
using SenseNet.ContentRepository.Storage.Data.MySqlClient;

namespace SenseNet.Extensions.DependencyInjection
{
    public static class MySqlExtensions
    {
        /// <summary>
        /// Adds MySQL implementations of data-related services to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetMySqlProviders(this IServiceCollection services,
            Action<ConnectionStringOptions> configureConnectionStrings = null,
            Action<MySqlDatabaseInstallationOptions> configureInstallation = null,
            Action<DataOptions> configureDataOptions = null)
        {
            return services.AddSenseNetMySqlDataProvider()
                .AddSingleton<ISharedLockDataProvider, MySqlSharedLockDataProvider>()
                .AddSingleton<IExclusiveLockDataProvider, MySqlExclusiveLockDataProvider>()
                .AddSingleton<IAccessTokenDataProvider, MySqlAccessTokenDataProvider>()
                .AddSingleton<IPackagingDataProvider, MySqlPackagingDataProvider>()
                .AddSenseNetMySqlStatisticalDataProvider()
                .AddDatabaseAuditEventWriter()
                .AddSenseNetMySqlClientStoreDataProvider()
                .Configure<ConnectionStringOptions>(options => configureConnectionStrings?.Invoke(options))
                .Configure<MySqlDatabaseInstallationOptions>(options => configureInstallation?.Invoke(options))
                .Configure<DataOptions>(options => configureDataOptions?.Invoke(options));
        }

        /// <summary>
        /// Adds the default MySQL data provider to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetMySqlDataProvider(this IServiceCollection services)
        {
            return services.AddSenseNetDataProvider<MySqlDataProvider>()
                .AddSenseNetDataInstaller<MySqlDataInstaller>()
                .AddSingleton<MySqlDatabaseInstaller>()
                .Configure<MySqlDatabaseInstallationOptions>(_ => { });
        }

        /// <summary>
        /// Adds the MySQL statistical data provider to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetMySqlStatisticalDataProvider(this IServiceCollection services)
        {
            return services.AddStatisticalDataProvider<MySqlStatisticalDataProvider>();
        }

        /// <summary>
        /// Adds the MySQL ClientStore data provider to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetMySqlClientStoreDataProvider(this IServiceCollection services)
        {
            return services.AddSenseNetClientStoreDataProvider<MySqlClientStoreDataProvider>();
        }
    }
}