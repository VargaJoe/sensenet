using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using STT = System.Threading.Tasks;
using Newtonsoft.Json;
using SenseNet.Diagnostics;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    /// <summary> 
    /// This is a MySQL implementation of the <see cref="IPackagingDataProvider"/> interface.
    /// It requires the main data provider to be a <see cref="RelationalDataProviderBase"/>.
    /// </summary>
    public class MySqlPackagingDataProvider : IPackagingDataProvider
    {
        private readonly RelationalDataProviderBase _mainProvider;

        public MySqlPackagingDataProvider(DataProvider mainProvider)
        {
            if (mainProvider == null)
                return;
            if (!(mainProvider is RelationalDataProviderBase relationalDataProviderBase))
                throw new ArgumentException("The mainProvider needs to be RelationalDataProviderBase.");
            _mainProvider = relationalDataProviderBase;
        }

        #region SQL LoadInstalledComponentsScript
        private static readonly string InstalledComponentsScript = @"
-- MySqlPackagingDataProvider.LoadInstalledComponents
SELECT ComponentId, PackageType, ComponentVersion, Description, Manifest
FROM Packages
WHERE (PackageType = @Install OR PackageType = @Patch) 
    AND ExecutionResult = @Successful
ORDER BY ComponentId, ComponentVersion, ExecutionDate
";
        #endregion

        public async STT.Task<IEnumerable<ComponentInfo>> LoadInstalledComponentsAsync(CancellationToken cancellationToken)
        {
            if (!(await _mainProvider.IsDatabaseReadyAsync(cancellationToken)))
                return new ComponentInfo[0];

            var components = new Dictionary<string, ComponentInfo>();
            var descriptions = new Dictionary<string, string>();

            using var op = SnTrace.Database.StartOperation("MySqlPackagingDataProvider: LoadInstalledComponents()");
            using var ctx = _mainProvider.CreateDataContext(cancellationToken);
            await ctx.ExecuteReaderAsync(InstalledComponentsScript,
                async (reader, cancel) =>
                {
                    cancel.ThrowIfCancellationRequested();
                    while (await reader.ReadAsync(cancel).ConfigureAwait(false))
                    {
                        cancel.ThrowIfCancellationRequested();

                        var component = new ComponentInfo
                        {
                            ComponentId = reader.GetSafeString(reader.GetOrdinal("ComponentId")),
                            Version = DecodePackageVersion(
                                reader.GetSafeString(reader.GetOrdinal("ComponentVersion"))),
                            Description = reader.GetSafeString(reader.GetOrdinal("Description")),
                            Manifest = reader.GetSafeString(reader.GetOrdinal("Manifest")),
                            ExecutionResult = ExecutionResult.Successful
                        };

                        components[component.ComponentId] = component;
                        if (reader.GetSafeString(reader.GetOrdinal("PackageType"))
                            == nameof(PackageType.Install))
                            descriptions[component.ComponentId] = component.Description;
                    }

                    return true;
                }).ConfigureAwait(false);

            foreach (var item in descriptions)
                components[item.Key].Description = item.Value;
            op.Successful = true;

            return components.Values.ToArray();
        }

        #region SQL LoadIncompleteComponentsScript
        private static readonly string IncompleteComponentsScript = @"
-- MySqlPackagingDataProvider.LoadIncompleteComponents
SELECT ComponentId, PackageType, ComponentVersion, Description, Manifest, ExecutionResult
FROM Packages
WHERE (PackageType = @Install OR PackageType = @Patch) 
    AND ExecutionResult != @Successful
ORDER BY ComponentId, ComponentVersion, ExecutionDate
";
        #endregion

        public async STT.Task<IEnumerable<ComponentInfo>> LoadIncompleteComponentsAsync(CancellationToken cancellationToken)
        {
            if (!(await _mainProvider.IsDatabaseReadyAsync(cancellationToken)))
                return new ComponentInfo[0];

            var components = new Dictionary<string, ComponentInfo>();
            var descriptions = new Dictionary<string, string>();

            using var op = SnTrace.Database.StartOperation("MySqlPackagingDataProvider: LoadIncompleteComponents()");
            using var ctx = _mainProvider.CreateDataContext(cancellationToken);
            await ctx.ExecuteReaderAsync(IncompleteComponentsScript,
                async (reader, cancel) =>
                {
                    cancel.ThrowIfCancellationRequested();
                    while (await reader.ReadAsync(cancel).ConfigureAwait(false))
                    {
                        cancel.ThrowIfCancellationRequested();

                        var src = reader.GetSafeString(reader.GetOrdinal("ExecutionResult"));
                        var executionResult = src == null
                            ? ExecutionResult.Unfinished
                            : (ExecutionResult)Enum.Parse(typeof(ExecutionResult), src);

                        var component = new ComponentInfo
                        {
                            ComponentId = reader.GetSafeString(reader.GetOrdinal("ComponentId")),
                            Version = DecodePackageVersion(
                                reader.GetSafeString(reader.GetOrdinal("ComponentVersion"))),
                            Description = reader.GetSafeString(reader.GetOrdinal("Description")),
                            Manifest = reader.GetSafeString(reader.GetOrdinal("Manifest")),
                            ExecutionResult = executionResult
                        };

                        components[component.ComponentId] = component;
                        if (reader.GetSafeString(reader.GetOrdinal("PackageType"))
                            == nameof(PackageType.Install))
                            descriptions[component.ComponentId] = component.Description;
                    }

                    return true;
                }).ConfigureAwait(false);

            foreach (var item in descriptions)
                components[item.Key].Description = item.Value;
            op.Successful = true;

            return components.Values.ToArray();
        }

        #region SQL SavePackageScript
        private static readonly string SavePackageScript = @"
INSERT INTO Packages 
    (Description, ComponentId, PackageType, ReleaseDate, ExecutionDate, ExecutionResult, ExecutionError, ComponentVersion, Manifest) 
VALUES 
    (@Description, @ComponentId, @PackageType, @ReleaseDate, @ExecutionDate, @ExecutionResult, @ExecutionError, @ComponentVersion, @Manifest);
SELECT LAST_INSERT_ID();
";
        #endregion

        public async STT.Task SavePackageAsync(Package package, CancellationToken cancellationToken)
        {
            using var op = SnTrace.Database.StartOperation("MySqlPackagingDataProvider: " +
                "SavePackage: ComponentId: {0}, ComponentVersion: {1}, ExecutionResult: {2}",
                package.ComponentId, package.ComponentVersion, package.ExecutionResult);

            using var ctx = _mainProvider.CreateDataContext(cancellationToken);
            var result = await ctx.ExecuteScalarAsync(SavePackageScript, cmd =>
            {
                cmd.Parameters.AddRange(new[]
                {
                    ctx.CreateParameter("@Description", DbType.String, 1000, (object)package.Description ?? DBNull.Value),
                    ctx.CreateParameter("@ComponentId", DbType.String, 50, (object)package.ComponentId ?? DBNull.Value),
                    ctx.CreateParameter("@PackageType", DbType.String, 50, package.PackageType.ToString()),
                    ctx.CreateParameter("@ReleaseDate", DbType.DateTime, package.ReleaseDate),
                    ctx.CreateParameter("@ExecutionDate", DbType.DateTime, package.ExecutionDate),
                    ctx.CreateParameter("@ExecutionResult", DbType.String, 50, package.ExecutionResult.ToString()),
                    ctx.CreateParameter("@ExecutionError", DbType.String, int.MaxValue, SerializeExecutionError(package.ExecutionError) ?? DBNull.Value),
                    ctx.CreateParameter("@ComponentVersion", DbType.String, 50,
                        package.ComponentVersion == null ? DBNull.Value : (object)EncodePackageVersion(package.ComponentVersion)),
                    ctx.CreateParameter("@Manifest", DbType.String, int.MaxValue, package.Manifest ?? DBNull.Value)
                });
            }).ConfigureAwait(false);

            package.Id = Convert.ToInt32(result);
            op.Successful = true;
        }

        // Additional methods (e.g., UpdatePackageAsync, DeletePackageAsync) should be updated similarly.
    }
}