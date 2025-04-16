using System;
using System.Threading;
using Microsoft.Extensions.Options;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Security.Clients;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.Diagnostics;
using SenseNet.Packaging;

namespace SenseNet.ContentRepository.Components
{
    public class MySqlClientStoreComponent : SnComponent
    {
        public override string ComponentId { get; } = "SenseNet.ClientStore.MySql";

        public override void AddPatches(PatchBuilder builder)
        {
            builder.Install("1.0.0", "2021-09-27", "MySQL implementation of Client store.")
                .DependsOn("SenseNet.Services", "7.7.23")
                .ActionOnBefore(context =>
                {
                    if (!(Providers.Instance.DataProvider is RelationalDataProviderBase dataProvider))
                        throw new InvalidOperationException("Cannot install MySqlClientStoreComponent because it is " +
                                                            $"incompatible with Data provider {Providers.Instance.DataProvider.GetType().FullName}.");

                    try
                    {
                        using var op = SnTrace.Database.StartOperation("MySqlClientStoreComponent: " +
                            "Install MySQL implementation of Client store (v1.0.0). " +
                            "Script name: MySqlClientStoreDataProvider.DropAndCreateTablesSql.");
                        using var ctx = dataProvider.CreateDataContext(CancellationToken.None);
                        ctx.ExecuteNonQueryAsync(MySqlClientStoreDataProvider.DropAndCreateTablesSql)
                            .GetAwaiter().GetResult();
                        op.Successful = true;
                    }
                    catch (Exception ex)
                    {
                        context.Log($"Error during installation of MySqlClientStore: {ex.Message}");
                        throw;
                    }
                })
                .Action(context =>
                {
                    // generate default clients and secrets
                    var clientStore = context.GetService<ClientStore>();
                    var clientOptions = context.GetService<IOptions<ClientStoreOptions>>().Value;
                    
                    clientStore.EnsureClientsAsync(clientOptions.Authority, clientOptions.RepositoryUrl.RemoveUrlSchema()).GetAwaiter().GetResult();
                });
        }
    }
}