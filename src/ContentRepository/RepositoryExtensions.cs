﻿using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Email;
using SenseNet.ContentRepository.Packaging;
using SenseNet.ContentRepository.Search.Indexing;
using SenseNet.ContentRepository.Security.ApiKeys;
using SenseNet.ContentRepository.Security.Cryptography;
using SenseNet.ContentRepository.Security.MultiFactor;
using SenseNet.ContentRepository.Storage;
using SenseNet.Diagnostics;
using SenseNet.Packaging;
using SenseNet.Preview;
using SenseNet.Search;
using SenseNet.Search.Indexing;
using SenseNet.Search.Querying;
using SenseNet.Storage.Security;

// ReSharper disable once CheckNamespace
namespace SenseNet.Extensions.DependencyInjection
{
    public static class RepositoryExtensions
    {
        /// <summary>
        /// Adds the default document provider to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetDocumentPreviewProvider(this IServiceCollection services)
        {
            // add the default, empty implementation
            return services.AddSenseNetDocumentPreviewProvider<DefaultDocumentPreviewProvider>();
        }
        /// <summary>
        /// Adds the provided document provider to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetDocumentPreviewProvider<T>(this IServiceCollection services) where T : DocumentPreviewProvider
        {
            return services.AddSingleton<IPreviewProvider, T>();
        }

        /// <summary>
        /// Adds the default <c>ILatestComponentStore</c> implementation to the service collection.
        /// </summary>
        public static IServiceCollection AddLatestComponentStore(this IServiceCollection services)
        {
            // add the default, empty implementation
            return services.AddLatestComponentStore<DefaultLatestComponentStore>();
        }
        /// <summary>
        /// Adds the provided <c>ILatestComponentStore</c> implementation to the service collection.
        /// Use this method when the default implementation
        /// (<c>SenseNet.ContentRepository.Packaging.DefaultLatestComponentStore</c>) needs to be replaced.
        /// </summary>
        public static IServiceCollection AddLatestComponentStore<T>(this IServiceCollection services)
            where T : class, ILatestComponentStore
        {
            return services.AddSingleton<ILatestComponentStore, T>();
        }

        /// <summary>
        /// Adds an <c>ISnComponent</c> to the service collection so that the system can
        /// collect components and their patches during repository start.
        /// </summary>
        public static IServiceCollection AddComponent(this IServiceCollection services, 
            Func<IServiceProvider, ISnComponent> createComponent)
        {
            // register this as transient so that no singleton instances remain in memory after creating them once
            services.AddTransient(createComponent);

            return services;
        }
        /// <summary>
        /// Adds an <c>ISnComponent</c> to the service collection so that the system can
        /// collect components and their patches during repository start.
        /// </summary>
        public static IServiceCollection AddComponent<T>(this IServiceCollection services) where T: class, ISnComponent
        {
            // register this as transient so that no singleton instances remain in memory after creating them once
            services.AddTransient<ISnComponent, T>();

            return services;
        }

        /// <summary>
        /// For internal use only.
        /// </summary>
        public static IServiceCollection AddRepositoryComponents(this IServiceCollection services)
        {
            services.AddComponent(provider => new ServicesComponent());

            return services;
        }
        
        /// <summary>
        /// Adds the installer information of the core sensenet package to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetInstallPackage(this IServiceCollection services, 
            Assembly assembly, string installPackageName)
        {
            services.AddSingleton<IInstallPackageDescriptor>(provider => new InstallPackageDescriptor(assembly, installPackageName));

            return services;
        }

        /// <summary>
        /// Adds the provided search engine to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetSearchEngine<T>(this IServiceCollection services) where T : class, ISearchEngine
        {
            return services.AddSingleton<ISearchEngine, T>();
        }
        /// <summary>
        /// Adds the provided search engine to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetSearchEngine(this IServiceCollection services, ISearchEngine searchEngine)
        {
            return services.AddSingleton(providers => searchEngine);
        }

        /// <summary>
        /// Adds the provided indexing engine to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetIndexingEngine<T>(this IServiceCollection services) where T : class, IIndexingEngine
        {
            return services.AddSingleton<IIndexingEngine, T>();
        }
        /// <summary>
        /// Adds the provided query engine to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetQueryEngine<T>(this IServiceCollection services) where T : class, IQueryEngine
        {
            return services.AddSingleton<IQueryEngine, T>();
        }

        /// <summary>
        /// Adds the provided api key manager to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetApiKeyManager<T>(this IServiceCollection services) where T : class, IApiKeyManager
        {
            return services.AddSingleton<IApiKeyManager, T>();
        }
        /// <summary>
        /// Adds the default api key manager to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetApiKeys(this IServiceCollection services)
        {
            return services.AddSenseNetApiKeyManager<ApiKeyManager>();
        }

        public static IServiceCollection AddSenseNetEmailManager(this IServiceCollection services, 
            Action<EmailOptions> configureSmtp = null)
        {
            return services
                .AddSingleton<IEmailTemplateManager, RepositoryEmailTemplateManager>()
                .AddSingleton<IEmailSender, EmailSender>()
                .Configure<EmailOptions>(options => { configureSmtp?.Invoke(options);});
        }


        /// <summary>
        /// Adds the provided template replacer to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetTemplateReplacer<T>(this IServiceCollection services) where T : TemplateReplacerBase
        {
            return services.AddSingleton<TemplateReplacerBase, T>();
        }

        /// <summary>
        /// Adds the provided crypto service provider to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetCryptoServiceProvider<T>(this IServiceCollection services) where T : class, ICryptoServiceProvider
        {
            return services.AddSingleton<ICryptoServiceProvider, T>()
                .Configure<CryptographyOptions>(opt => {});
        }

        /// <summary>
        /// Adds default sensenet repository services to the service collection.
        /// </summary>
        public static IServiceCollection AddSenseNetDefaultRepositoryServices(this IServiceCollection services)
        {
            return services.AddSenseNetCryptoServiceProvider<DefaultCryptoServiceProvider>();
        }

        /// <summary>
        /// Adds the provided custom <c>ISnTracer</c> implementation type to the service collection.
        /// Available built-in tracers: <c>SnDebugViewTracer</c>, <c>SnFileSystemTracer</c>, <c>SnILoggerTracer</c>
        /// </summary>
        public static IServiceCollection AddSenseNetTracer<T>(this IServiceCollection services) where T : class, ISnTracer
        {
            return services.AddSingleton<ISnTracer, T>();
        }

        /// <summary>
        /// Assigns the provided text extractor type to the given file extension and adds the assignment to the service collection.
        /// </summary>
        /// <typeparam name="TImpl">Type of the <see cref="ITextExtractor"/> implementation.</typeparam>
        /// <param name="services"></param>
        /// <param name="fileExtension">File extension (e.g. "txt").</param>
        /// <returns></returns>
        public static IServiceCollection AddTextExtractor<TImpl>(this IServiceCollection services, string fileExtension) where TImpl : class, ITextExtractor
        {
            services.AddSingleton<ITextExtractor, TImpl>();
            return services.AddSingleton<TextExtractorRegistration>(new TextExtractorRegistration
            {
                FileExtension = fileExtension.TrimStart('.'),
                TextExtractorType = typeof(TImpl)
            });
        }

        /// <summary>
        /// For internal use only.
        /// </summary>
        public static IServiceCollection AddDefaultTextExtractors(this IServiceCollection services)
        {
            return services
                    .AddTextExtractor<XmlTextExtractor>("contenttype")
                    .AddTextExtractor<XmlTextExtractor>("xml")
                    .AddTextExtractor<DocTextExtractor>("doc")
                    .AddTextExtractor<XlsTextExtractor>("xls")
                    .AddTextExtractor<XlbTextExtractor>("xlb")
                    .AddTextExtractor<MsgTextExtractor>("msg")
                    .AddTextExtractor<PdfTextExtractor>("pdf")
                    .AddTextExtractor<DocxTextExtractor>("docx")
                    .AddTextExtractor<DocxTextExtractor>("docm")
                    .AddTextExtractor<XlsxTextExtractor>("xlsx")
                    .AddTextExtractor<XlsxTextExtractor>("xlsm")
                    .AddTextExtractor<PptxTextExtractor>("pptx")
                    .AddTextExtractor<PlainTextExtractor>("txt")
                    .AddTextExtractor<PlainTextExtractor>("settings")
                    .AddTextExtractor<RtfTextExtractor>("rtf")
                ;
        }

        /// <summary>
        /// Adds the default multifactor authentication provider to the service collection.
        /// </summary>
        public static IServiceCollection AddDefaultMultiFactorAuthenticationProvider(this IServiceCollection services)
        {
            return services.AddMultiFactorAuthenticationProvider<DefaultMultiFactorProvider>();
        }
        /// <summary>
        /// Adds the provided multifactor authentication provider to the service collection.
        /// </summary>
        public static IServiceCollection AddMultiFactorAuthenticationProvider<T>(this IServiceCollection services)
            where T : class, IMultiFactorAuthenticationProvider
        {
            return services.AddSingleton<IMultiFactorAuthenticationProvider, T>();
        }
    }
}
