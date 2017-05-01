// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol.Plugins;
using NuGet.Versioning;

namespace NuGet.Protocol.Core.Types
{
    /// <summary>
    /// A FindPackageByIdResource for plugins.
    /// </summary>
    public sealed class PluginFindPackageByIdResource : FindPackageByIdResource
    {
        private PluginCredentialProvider _credentialProvider;
        private readonly PluginResource _pluginResource;
        private readonly PackageSource _packageSource;

        /// <summary>
        /// Instantiates a new <see cref="PluginFindPackageByIdResource" /> class.
        /// </summary>
        /// <param name="pluginResource">A plugin resource.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="pluginResource" />
        /// is <c>null</c>.</exception>
        public PluginFindPackageByIdResource(PluginResource pluginResource, PackageSource packageSource, PluginCredentialProvider credentialProvider)
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
        /// Asynchronously copies a package to the specified stream.
        /// </summary>
        /// <param name="id">The package ID.</param>
        /// <param name="version">The package version.</param>
        /// <param name="destination">The destination stream for the copy operation.</param>
        /// <param name="cacheContext">A source cache context.</param>
        /// <param name="logger">A logger.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.
        /// The task result (<see cref="Task{TResult}.Result" />) returns a
        /// <see cref="bool" /> indicating the result of the copy operation.</returns>
        public override Task<bool> CopyNupkgToStreamAsync(
            string id,
            NuGetVersion version,
            Stream destination,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public override async Task CopyPackageAsync(
            string name,
            NuGetVersion version,
            VersionFolderPathContext versionFolderPathContext,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            using (var plugin = await _pluginResource.GetPluginAsync(OperationClaim.DownloadPackage, logger, cancellationToken))
            {
                var packagePathResolver = new VersionFolderPathResolver(
                    versionFolderPathContext.PackagesDirectory,
                    versionFolderPathContext.IsLowercasePackagesDirectory);

                var request = new DownloadPackageRequest(
                    _packageSource.Source,
                    name,
                    version.ToNormalizedString(),
                    packagePathResolver.GetPackageDirectory(name, version),
                    versionFolderPathContext.PackageSaveMode,
                    versionFolderPathContext.XmlDocFileSaveMode,
                    packagePathResolver.GetHashPath(name, version),
                    "SHA512");

                var response = await plugin.Connection.SendRequestAndReceiveResponseAsync<DownloadPackageRequest, DownloadPackageResponse>(
                    MessageMethod.DownloadPackage,
                    request,
                    cancellationToken);

                if (response.ResponseCode != MessageResponseCode.Success)
                {
                    throw new PluginException("Copy package failed");
                }
            }
        }

        /// <summary>
        /// Asynchronously gets all versions of a package.
        /// </summary>
        /// <param name="id">The package ID.</param>
        /// <param name="cacheContext">A source cache context.</param>
        /// <param name="logger">A logger.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.
        /// The task result (<see cref="Task{TResult}.Result" />) returns a
        /// <see cref="IEnumerable{NuGetVersion}" />.</returns>
        public override async Task<IEnumerable<NuGetVersion>> GetAllVersionsAsync(
            string id,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken token)
        {
            using (var plugin = await _pluginResource.GetPluginAsync(OperationClaim.DownloadPackage, logger, token))
            {
                var request = new GetPackageVersionsRequest(_packageSource.Source, id);

                var response = await plugin.Connection.SendRequestAndReceiveResponseAsync<GetPackageVersionsRequest, GetPackageVersionsResponse>(
                    MessageMethod.GetPackageVersions,
                    request,
                    token);

                if (response.ResponseCode == MessageResponseCode.Success)
                {
                    var versions = response.Versions.Select(v => NuGetVersion.Parse(v));

                    return versions;
                }

                throw new ProtocolException($"Could not get all package versions for package {id}.");
            }
        }

        /// <summary>
        /// Asynchronously gets package dependency information.
        /// </summary>
        /// <param name="id">The package ID.</param>
        /// <param name="version">The package version.</param>
        /// <param name="cacheContext">A source cache context.</param>
        /// <param name="logger">A logger.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.
        /// The task result (<see cref="Task{TResult}.Result" />) returns a
        /// <see cref="FindPackageByIdDependencyInfo" />.</returns>
        public override async Task<FindPackageByIdDependencyInfo> GetDependencyInfoAsync(
            string id,
            NuGetVersion version,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken token)
        {
            using (var plugin = await _pluginResource.GetPluginAsync(OperationClaim.DownloadPackage, logger, token))
            {
                throw new NotImplementedException();
            }
        }
    }
}