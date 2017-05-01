// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.Plugins;

namespace NuGet.Protocol
{
    /// <summary>
    /// A download resource for plugins.
    /// </summary>
    public sealed class DownloadResourcePlugin : DownloadResource
    {
        private PluginCredentialProvider _credentialProvider;
        private readonly PluginResource _pluginResource;
        private readonly PackageSource _packageSource;

        /// <summary>
        /// Instantiates a new <see cref="DownloadResourcePlugin" /> class.
        /// </summary>
        /// <param name="pluginResource">A plugin resource.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="pluginResource" />
        /// is <c>null</c>.</exception>
        public DownloadResourcePlugin(PluginResource pluginResource, PackageSource packageSource, PluginCredentialProvider credentialProvider)
        {
            if (pluginResource == null)
            {
                throw new ArgumentNullException(nameof(pluginResource));
            }

            _pluginResource = pluginResource;
            _packageSource = packageSource;
            _credentialProvider = credentialProvider;
        }

        /// <summary>
        /// Asynchronously downloads a package.
        /// </summary>
        /// <param name="identity">The package identity.</param>
        /// <param name="downloadContext">A package download context.</param>
        /// <param name="globalPackagesFolder">The path to the global packages folder.</param>
        /// <param name="logger">A logger.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.
        /// The task result (<see cref="Task{TResult}.Result" />) returns
        /// a <see cref="DownloadResourceResult" />.</returns>
        public async override Task<DownloadResourceResult> GetDownloadResourceResultAsync(
            PackageIdentity identity,
            PackageDownloadContext downloadContext,
            string globalPackagesFolder,
            ILogger logger,
            CancellationToken token)
        {
            var plugin = await _pluginResource.GetPluginAsync(OperationClaim.DownloadPackage, logger, token);

            TryAddLogger(plugin, logger);

            _credentialProvider = TryUpdateCredentialProvider(plugin);

            await plugin.Connection.SendRequestAndReceiveResponseAsync<PrefetchPackageRequest, PrefetchPackageResponse>(
                MessageMethod.PrefetchPackage,
                new PrefetchPackageRequest(_packageSource.Source, identity.Id, identity.Version.ToNormalizedString()),
                token);
            var packageReader = new PluginPackageReader(plugin, identity, _packageSource.Source);

            return new DownloadResourceResult(packageReader, _packageSource.Source);
        }

        private void TryAddLogger(IPlugin plugin, ILogger logger)
        {
            plugin.Connection.MessageDispatcher.RequestHandlers.TryAdd(MessageMethod.Log, new LogRequestHandler(logger, LogLevel.Debug));
        }

        private PluginCredentialProvider TryUpdateCredentialProvider(IPlugin plugin)
        {
            if (plugin.Connection.MessageDispatcher.RequestHandlers.TryAdd(MessageMethod.GetCredential, _credentialProvider))
            {
                return _credentialProvider;
            }

            IRequestHandler handler;

            if (plugin.Connection.MessageDispatcher.RequestHandlers.TryGet(MessageMethod.GetCredential, out handler))
            {
                return (PluginCredentialProvider)handler;
            }

            throw new InvalidOperationException();
        }
    }
}