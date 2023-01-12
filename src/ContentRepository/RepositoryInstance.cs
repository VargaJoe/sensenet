﻿using System;
using System.Collections.Generic;
using System.Linq;
using SenseNet.ContentRepository.i18n;
using SenseNet.ContentRepository.Storage;
using System.Reflection;
using SenseNet.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using SenseNet.Communication.Messaging;
using SenseNet.Configuration;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Storage.Security;
using SenseNet.Search.Querying;
using SenseNet.TaskManagement.Core;
using SenseNet.Tools;

namespace SenseNet.ContentRepository
{
    /// <summary>
    /// Represents a running Repository. There is always one instance in any appdomain.
    /// Repository will be stopped when the instance is disposing.
    /// </summary>
    public sealed class RepositoryInstance : IDisposable
    {
        /// <summary>
        /// Provides some information about the boot sequence
        /// </summary>
        public class StartupInfo
        {
            /// <summary>
            /// Name of the assemblies thats are loaded before startup sequence begins.
            /// </summary>
            public string[] AssembliesBeforeStart { get; internal set; }
            /// <summary>
            /// Name of the assemblies thats are loaded from the appdomain's working directory.
            /// </summary>
            public string[] ReferencedAssemblies { get; internal set; }
            /// <summary>
            /// Name of the assemblies thats are loaded from an additional path (if there is).
            /// </summary>
            public string[] Plugins { get; internal set; }
            /// <summary>
            /// Moment of the start before executing the startup sequence.
            /// </summary>
            public DateTime Starting { get; internal set; }
            /// <summary>
            /// Moment of the start after executing the startup sequence.
            /// </summary>
            public DateTime Started { get; internal set; }
        }

        private StartupInfo _startupInfo;
        private RepositoryStartSettings.ImmutableRepositoryStartSettings _settings;
        private static RepositoryInstance _instance;
        private static object _startStopSync = new object();

        /// <summary>
        /// Gets a <see cref="StartupInfo"/> instance that provides some information about the boot sequence.
        /// </summary>
        public StartupInfo StartupTrace { get { return _startupInfo; } }
        /// <summary>
        /// Gets the startup control information.
        /// </summary>
        [Obsolete("Use individual immutable properties instead.")]
        public RepositoryStartSettings.ImmutableRepositoryStartSettings StartSettings => _settings;

        /// <summary>
        /// Gets the started up instance or null.
        /// </summary>
        public static RepositoryInstance Instance { get { return _instance; } }

        public TextWriter Console => _settings?.Console;

        private RepositoryInstance()
        {
            _startupInfo = new StartupInfo { Starting = DateTime.UtcNow };
        }

        private static bool _started;
        internal static RepositoryInstance Start(RepositoryStartSettings settings)
        {
            if (!_started)
            {
                lock (_startStopSync)
                {
                    if (!_started)
                    {
                        var instance = new RepositoryInstance();
                        instance._settings = new RepositoryStartSettings.ImmutableRepositoryStartSettings(settings);
                        _instance = instance;
                        try
                        {
                            instance.DoStart();
                        }
                        catch (Exception)
                        {
                            _instance = null;
                            throw;
                        }
                        _started = true;
                    }
                }
            }
            return _instance;
        }
        internal void DoStart()
        {
            ConsoleWriteLine();
            ConsoleWriteLine("Starting Repository...");
            ConsoleWriteLine();

            LoggingSettings.SnTraceConfigurator.ConfigureCategories();
            if (_settings.TraceCategories != null)
                LoggingSettings.SnTraceConfigurator.UpdateCategories(_settings.TraceCategories);

            InitializeLogger();

            RegisterAppdomainEventHandlers();

            if (_settings.IndexPath != null)
                Providers.Instance.SearchManager.IndexDirectoryPath = _settings.IndexPath;

            LoadAssemblies(_settings.IsWebContext);

            _settings.Services.GetRequiredService<SecurityHandler>().StartSecurity(_settings.IsWebContext, _settings.Services);

            //UNDONE: modernize TemplateManager
            // Set legacy collection from the new services collection and reset the
            // current singleton list to force the system to regenerate it. 
            TemplateManager.TemplateReplacerInstances =
                _settings.Services.GetService<IEnumerable<TemplateReplacerBase>>()?.ToArray() ??
                Array.Empty<TemplateReplacerBase>();
            TemplateManager.Clear();

            SnQueryVisitor.VisitorExtensionTypes = new[] {typeof(Sharing.SharingVisitor)};

            // We have to log the access provider here because it cannot be logged 
            // during creation as it would lead to a circular reference.
            SnLog.WriteInformation($"AccessProvider created: {AccessProvider.Current?.GetType().FullName}");

            using (new SystemAccount())
                StartManagers();

            LoggingSettings.SnTraceConfigurator.UpdateCategoriesBySettings();
            
            ConsoleWriteLine();
            ConsoleWriteLine("Repository has started.");
            ConsoleWriteLine();

            _startupInfo.Started = DateTime.UtcNow;
        }

        /// <summary>
        /// Starts IndexingEngine if it is not running.
        /// </summary>
        public void StartIndexingEngine()
        {
            if (IndexingEngineIsRunning)
            {
                ConsoleWrite("IndexingEngine has already started.");
                return;
            }
            ConsoleWriteLine("Starting IndexingEngine:");
            Providers.Instance.IndexManager.StartAsync(_settings.Console, CancellationToken.None).GetAwaiter().GetResult();
            Providers.Instance.SearchManager.SearchEngine.SetIndexingInfo(ContentTypeManager.Instance.IndexingInfo);
            ConsoleWriteLine("IndexingEngine has started.");
        }

        private bool _workflowEngineIsRunning;
        /// <summary>
        /// Starts workflow engine if it is not running.
        /// </summary>
        public void StartWorkflowEngine()
        {
            if (_workflowEngineIsRunning)
            {
                ConsoleWrite("Workflow engine has already started.");
                return;
            }
            ConsoleWrite("Starting Workflow subsystem ... ");
            var t = TypeResolver.GetType("SenseNet.Workflow.InstanceManager", false);
            if (t != null)
            {
                var m = t.GetMethod("StartWorkflowSystem", BindingFlags.Static | BindingFlags.Public);
                m.Invoke(null, new object[0]);
                _workflowEngineIsRunning = true;
                ConsoleWriteLine("ok.");
            }
            else
            {
                ConsoleWriteLine("NOT STARTED");
            }
        }

        private void LoadAssemblies(bool isWebContext)
        {
            string[] asmNames;
            _startupInfo.AssembliesBeforeStart = GetLoadedAsmNames().ToArray();
            var localBin = AppDomain.CurrentDomain.BaseDirectory;
            var pluginsPath = _settings.PluginsPath ?? localBin;

            if (!isWebContext)
            {
                ConsoleWriteLine("Loading Assemblies from ", localBin, ":");
                asmNames = TypeResolver.LoadAssembliesFrom(localBin);
                foreach (string name in asmNames)
                    ConsoleWriteLine("  ", name);
            }

            _startupInfo.ReferencedAssemblies = GetLoadedAsmNames().Except(_startupInfo.AssembliesBeforeStart).ToArray();

            ConsoleWriteLine("Loading Assemblies from ", pluginsPath, ":");
            asmNames = TypeResolver.LoadAssembliesFrom(pluginsPath);
            _startupInfo.Plugins = GetLoadedAsmNames().Except(_startupInfo.AssembliesBeforeStart).Except(_startupInfo.ReferencedAssemblies).ToArray();

            if (_settings.Console == null)
                return;

            foreach (string name in asmNames)
                ConsoleWriteLine("  ", name);
            ConsoleWriteLine("Ok.");
            ConsoleWriteLine();
        }
        private IEnumerable<string> GetLoadedAsmNames()
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.FullName).ToArray();
        }
        private void StartManagers()
        {
            object dummy;
            IClusterChannel channel = null;

            try
            {
                ConsoleWrite("Initializing cache ... ");
                dummy = Cache.Count;
                
                // Log this, because logging is switched off when creating the cache provider
                // to avoid circular reference.
                SnLog.WriteInformation($"CacheProvider created: {Cache.Instance?.GetType().FullName}");
                ConsoleWriteLine("ok.");

                ConsoleWrite("Starting message channel ... ");
                channel = Providers.Instance.ClusterChannelProvider;
                channel.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
                
                ConsoleWriteLine("ok.");
                SnLog.WriteInformation($"Message channel {channel.GetType().FullName} started." +
                                       $"Instance id: {channel.ClusterMemberInfo.InstanceID}");

                ConsoleWrite("Sending greeting message ... ");
                new PingMessage(new string[0]).SendAsync(CancellationToken.None).GetAwaiter().GetResult();
                ConsoleWriteLine("ok.");

                ConsoleWrite("Starting NodeType system ... ");
                dummy = Providers.Instance.StorageSchema.NodeTypes[0];
                ConsoleWriteLine("ok.");

                ConsoleWrite("Starting ContentType system ... ");
                dummy = ContentType.GetByName("GenericContent");
                ConsoleWriteLine("ok.");

                ConsoleWrite("Starting AccessProvider ... ");
                dummy = User.Current;
                ConsoleWriteLine("ok.");

                SnQuery.SetPermissionFilterFactory(Providers.Instance.PermissionFilterFactory);

                if (_settings.StartIndexingEngine)
                    StartIndexingEngine();
                else
                    ConsoleWriteLine("IndexingEngine is not started.");

                // switch on message processing after IndexingEngine was started.
                channel.AllowMessageProcessing = true;

                if (_settings.StartWorkflowEngine)
                    StartWorkflowEngine();
                else
                    ConsoleWriteLine("Workflow subsystem is not started.");

                ConsoleWrite("Loading string resources ... ");
                dummy = SenseNetResourceManager.Current;
                ConsoleWriteLine("ok.");

                _serviceInstances = _settings.Services.GetServices<ISnService>().ToList();

                foreach (var service in _serviceInstances)
                {
                    service.Start();
                    ConsoleWriteLine("Service started: ", service.GetType().Name);
                }

                // register this application in the task management component
                var taskManager = _settings.Services.GetRequiredService<ITaskManager>();
                taskManager.RegisterApplicationAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // If an error occurred, shut down the cluster channel.
                channel?.ShutDownAsync(CancellationToken.None).GetAwaiter().GetResult();

                throw;
            }
        }

        private List<ISnService> _serviceInstances;

        private static void InitializeLogger()
        {
            // look for the configured logger
            SnLog.Instance = Providers.Instance.EventLogger ?? new DebugWriteLoggerAdapter();
            SnLog.AuditEventWriter = Providers.Instance.AuditEventWriter ?? throw new ApplicationException("Missing AuditEventWriter service.");

            //set configured tracers
            var tracers = Providers.Instance.Services.GetServices<ISnTracer>().ToArray();
            if (tracers.Length > 0)
            {
                SnTrace.SnTracers.Clear();
                SnTrace.SnTracers.AddRange(tracers);
            }

            SnLog.WriteInformation("Loggers and tracers initialized.", properties: new Dictionary<string, object>
            {
                { "Loggers", SnLog.Instance?.GetType().Name },
                { "Tracers", string.Join(", ", SnTrace.SnTracers.Select(snt => snt?.GetType().Name)) }
            });
        }

        private void RegisterAppdomainEventHandlers()
        {
            AppDomain appDomain = AppDomain.CurrentDomain;
            appDomain.UnhandledException += new UnhandledExceptionEventHandler(Domain_UnhandledException);
        }

        private void Domain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e?.ExceptionObject as Exception;
            if(ex != null)
                SnLog.WriteException(ex, "Domain_UnhandledException", EventId.NotDefined);
            else
                SnLog.WriteError("Domain_UnhandledException. ExceptionObject is " + e?.ExceptionObject ?? "null", EventId.NotDefined);
        }
        private Assembly Domain_TypeResolve(object sender, ResolveEventArgs args)
        {
            SnTrace.System.Write("Domain_TypeResolve: " + args.Name);
            return null;
        }
        private Assembly Domain_ResourceResolve(object sender, ResolveEventArgs args)
        {
            SnTrace.System.Write("Domain_ResourceResolve: " + args.Name);
            return null;
        }
        private Assembly Domain_ReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args)
        {
            SnTrace.System.Write("Domain_ReflectionOnlyAssemblyResolve: " + args.Name);
            return null;
        }
        private Assembly Domain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            SnTrace.System.Write("Domain_AssemblyResolve: " + args.Name);
            return null;
        }
        private void Domain_AssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            SnTrace.System.Write("Domain_AssemblyLoad: " + args.LoadedAssembly.FullName);
        }

        internal static void Shutdown()
        {
            if (_instance == null)
            {
                _started = false;

                SnLog.WriteWarning("Repository shutdown has already completed.");
                return;
            }

            lock (_startStopSync)
            {
                if (_instance == null)
                {
                    _started = false;

                    SnLog.WriteWarning("Repository shutdown has already completed.");
                    return;
                }

                SnTrace.Repository.Write("Sending a goodbye message.");

                _instance.ConsoleWriteLine();

                _instance.ConsoleWriteLine("Sending a goodbye message...");
                var channel = Providers.Instance.ClusterChannelProvider;
                channel.ClusterMemberInfo.NeedToRecover = false;
                var pingMessage = new PingMessage();
                pingMessage.SendAsync(CancellationToken.None).GetAwaiter().GetResult();

                foreach (var svc in _instance._serviceInstances)
                {
                    SnTrace.Repository.Write("Shutting down {0}", svc.GetType().Name);
                    svc.Shutdown();
                }

                SnTrace.Repository.Write("Shutting down {0}", channel.GetType().Name);
                channel.ShutDownAsync(CancellationToken.None).GetAwaiter().GetResult();

                SnTrace.Repository.Write("Shutting down Security.");
                Providers.Instance.SecurityHandler.ShutDownSecurity();

                SnTrace.Repository.Write("Shutting down IndexingEngine.");
                Providers.Instance.IndexManager.ShutDown();

                ContextHandler.Reset();

                var t = DateTime.UtcNow - _instance._startupInfo.Starting;
                var msg = $"Repository has stopped. Running time: {t.Days}.{t.Hours:d2}:{t.Minutes:d2}:{t.Seconds:d2}";

                SnTrace.Repository.Write(msg);
                SnTrace.Flush();

                _instance.ConsoleWriteLine(msg);
                _instance.ConsoleWriteLine();
                SnLog.WriteInformation(msg);

                _instance = null;
                _started = false;
            }
        }

        public void ConsoleWrite(params string[] text)
        {
            foreach (var s in text)
            {
                SnTrace.System.Write(s);
                _settings.Console?.Write(s);
            }
        }
        public void ConsoleWriteLine(params string[] text)
        {
            ConsoleWrite(text);
            _settings.Console?.WriteLine();
        }

        internal static bool Started()
        {
            return _started;
        }

        // ======================================== IndexingEngine hooks

        public static bool IndexingEngineIsRunning
        {
            get
            {
                if (_instance == null)
                    throw new NotSupportedException("Querying running state of IndexingEngine is not supported when RepositoryInstance is not created.");
                return Providers.Instance.IndexManager.Running;
            }
        }

        // ======================================== IDisposable
        private bool _disposed;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        private void Dispose(bool disposing)
        {
            if (!this._disposed)
                if (disposing)
                    Shutdown();
            _disposed = true;
        }
        ~RepositoryInstance()
        {
            Dispose(false);
        }
    }
}
