using System;
using System.Threading;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.Diagnostics;
using SenseNet.Packaging;
using SenseNet.Storage.Data.MySqlClient;

namespace SenseNet.ContentRepository.Components
{
    public class MySqlStatisticsComponent : SnComponent
    {
        public override string ComponentId { get; } = "SenseNet.Statistics.MySql";

        public override void AddPatches(PatchBuilder builder)
        {
            builder.Install("1.0.0", "2020-06-22", "MySQL data provider extension for the statistical data handling feature.")
                .DependsOn("SenseNet.Services", "7.7.22")
                .ActionOnBefore(context =>
                {
                    var dataStore = Providers.Instance.DataStore;
                    if (!(dataStore.DataProvider is RelationalDataProviderBase dataProvider))
                        throw new InvalidOperationException("Cannot install MySqlStatisticsComponent because it is " +
                                                            $"incompatible with Data provider {dataStore.DataProvider.GetType().FullName}.");

                    try
                    {
                        using var op = SnTrace.Database.StartOperation("MySqlStatisticsComponent: " +
                            "Install MySQL data provider extension for the statistical data handling feature (v1.0.0). " +
                            "Script name: MySqlStatisticalDataProvider.CreationScript");
                        using var ctx = dataProvider.CreateDataContext(CancellationToken.None);
                        ctx.ExecuteNonQueryAsync(MySqlStatisticalDataProvider.CreationScript).GetAwaiter().GetResult();
                        op.Successful = true;
                    }
                    catch (Exception ex)
                    {
                        context.Log($"Error during installation of MySqlStatisticsComponent: {ex.Message}");
                        throw;
                    }
                });
        }
    }
}