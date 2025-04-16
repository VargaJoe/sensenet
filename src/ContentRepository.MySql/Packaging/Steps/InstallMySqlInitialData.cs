using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage.Data.MySqlClient;
using SenseNet.ContentRepository.Storage.DataModel;
using SenseNet.Storage.Data.MySqlClient;
using SenseNet.Tools;

// ReSharper disable CheckNamespace

namespace SenseNet.Packaging.Steps
{
    public class InstallMySqlInitialData : Step
    {
        public string ConnectionName { get; set; }
        public string DataSource { get; set; }
        public string InitialCatalogName { get; set; }
        public InitialCatalog InitialCatalog { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }

        public override void Execute(ExecutionContext context)
        {
            var connectionInfo = new ConnectionInfo
            {
                ConnectionName = (string)context.ResolveVariable(ConnectionName),
                DataSource = (string)context.ResolveVariable(DataSource),
                InitialCatalog = InitialCatalog,
                InitialCatalogName = (string)context.ResolveVariable(InitialCatalogName),
                UserName = (string)context.ResolveVariable(UserName),
                Password = (string)context.ResolveVariable(Password)
            };
            var connectionString = MySqlDataContext.GetConnectionString(connectionInfo, context.ConnectionStrings);

            var initialData = InitialData.Load(new SenseNetServicesInitialData(), null);
            var dataOptions = Options.Create(DataOptions.GetLegacyConfiguration());
            var connOptions = Options.Create(new ConnectionStringOptions
            {
                Repository = connectionString
            });
            
            var installer = new MySqlDataInstaller(connOptions, NullLoggerFactory.Instance.CreateLogger<MySqlDataInstaller>());
            var logger = GetService<ILogger<MySqlDataProvider>>();
            var loggerForDbInstaller = GetService<ILogger<MySqlDatabaseInstaller>>();
            var dbInstallerOptions = GetService<IOptions<MySqlDatabaseInstallationOptions>>();
            var dataProvider = new MySqlDataProvider(dataOptions, connOptions,
                dbInstallerOptions, new MySqlDatabaseInstaller(dbInstallerOptions, loggerForDbInstaller),
                installer, logger, GetService<IRetrier>());            

            installer.InstallInitialDataAsync(initialData, dataProvider, CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}