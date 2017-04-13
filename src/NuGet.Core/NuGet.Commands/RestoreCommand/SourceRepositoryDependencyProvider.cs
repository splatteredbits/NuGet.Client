// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NuGet.Commands
{
    public class SourceRepositoryDependencyProvider : IRemoteDependencyProvider
    {
        private readonly object _lock = new object();
        private readonly SourceRepository _sourceRepository;
        private readonly ILogger _logger;
        private readonly SourceCacheContext _cacheContext;
        private FindPackageByIdResource _findPackagesByIdResource;
        private bool _ignoreFailedSources;
        private bool _ignoreWarning;

        // Limiting concurrent requests to limit the amount of files open at a time on Mac OSX
        // the default is 256 which is easy to hit if we don't limit concurrency
        private readonly static SemaphoreSlim _throttle =
            RuntimeEnvironmentHelper.IsMacOSX
                ? new SemaphoreSlim(ConcurrencyLimit, ConcurrencyLimit)
                : null;

        // In order to avoid too many open files error, set concurrent requests number to 16 on Mac
        private const int ConcurrencyLimit = 16;

        public SourceRepositoryDependencyProvider(
            SourceRepository sourceRepository,
            ILogger logger,
            SourceCacheContext cacheContext,
            bool ignoreFailedSources,
            bool ignoreWarning)
        {
            _sourceRepository = sourceRepository;
            _logger = logger;
            _cacheContext = cacheContext;
            _ignoreFailedSources = ignoreFailedSources;
            _ignoreWarning = ignoreWarning;
        }

        public bool IsHttp => _sourceRepository.PackageSource.IsHttp;

        /// <summary>
        /// Discovers all versions of a package from a source and selects the best match.
        /// </summary>
        public async Task<LibraryIdentity> FindLibraryAsync(
            LibraryRange libraryRange,
            NuGetFramework targetFramework,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            await EnsureResource();

            LibraryIdentity resolvedLibrary = null;
            var currentCacheContext = cacheContext;

            // Allow two attempts, the second attempt will invalidate the cache and try again.
            for (var i = 0; i < 2 && resolvedLibrary == null; i++)
            {
                try
                {
                    resolvedLibrary = await GetLibraryIdentityAsync(libraryRange, targetFramework, currentCacheContext, logger, cancellationToken);
                }
                catch (PackageNotFoundProtocolException ex)
                {
                    if (i == 0)
                    {
                        // 1st failure, invalidate the cache and try again.
                        // Clear the on disk and memory caches during the next request.
                        currentCacheContext = cacheContext.Clone();
                        currentCacheContext.MaxAge = DateTimeOffset.UtcNow;
                        currentCacheContext.RefreshMemoryCache = true;

                        logger.LogDebug($"Failed to download package {libraryRange.Name} from feed. Clearing the cache for this package and trying again.");
                    }
                    else
                    {
                        // 2nd failure, the feed is likely corrupt or removing packages too fast to keep up with.
                        var message = string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.Error_PackageNotFoundWhenExpected,
                            _sourceRepository.PackageSource.Source,
                            ex.PackageIdentity.ToString());

                        throw new FatalProtocolException(message, ex);
                    }
                }
            }

            return resolvedLibrary;
        }

        public async Task<IEnumerable<LibraryDependency>> GetDependenciesAsync(
            LibraryIdentity match,
            NuGetFramework targetFramework,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            await EnsureResource();

            FindPackageByIdDependencyInfo packageInfo = null;
            try
            {
                if (_throttle != null)
                {
                    await _throttle.WaitAsync();
                }
                packageInfo = await _findPackagesByIdResource.GetDependencyInfoAsync(
                    match.Name,
                    match.Version,
                    cacheContext,
                    logger,
                    cancellationToken);
            }
            catch (FatalProtocolException e) when (_ignoreFailedSources)
            {
                if (!_ignoreWarning)
                {
                    _logger.LogWarning(e.Message);
                }
                return new List<LibraryDependency>();
            }
            finally
            {
                _throttle?.Release();
            }

            return GetDependencies(packageInfo, targetFramework);
        }

        public async Task CopyToAsync(
            LibraryIdentity identity,
            Stream stream,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            await EnsureResource();

            try
            {
                if (_throttle != null)
                {
                    await _throttle.WaitAsync();
                }

                cancellationToken.ThrowIfCancellationRequested();

                // If the stream is already available, do not stop in the middle of copying the stream
                // Pass in CancellationToken.None
                await _findPackagesByIdResource.CopyNupkgToStreamAsync(
                    identity.Name,
                    identity.Version,
                    stream,
                    cacheContext,
                    logger,
                    CancellationToken.None);
            }
            catch (FatalProtocolException e) when (_ignoreFailedSources)
            {
                if (!_ignoreWarning)
                {
                    _logger.LogWarning(e.Message);
                }
            }
            finally
            {
                _throttle?.Release();
            }
        }

        private IEnumerable<LibraryDependency> GetDependencies(FindPackageByIdDependencyInfo packageInfo, NuGetFramework targetFramework)
        {
            if (packageInfo == null)
            {
                return new List<LibraryDependency>();
            }
            var dependencies = NuGetFrameworkUtility.GetNearest(packageInfo.DependencyGroups,
                targetFramework,
                item => item.TargetFramework);

            return GetDependencies(targetFramework, dependencies);
        }

        private static IList<LibraryDependency> GetDependencies(NuGetFramework targetFramework,
            PackageDependencyGroup dependencies)
        {
            var libraryDependencies = new List<LibraryDependency>();

            if (dependencies != null)
            {
                libraryDependencies.AddRange(
                    dependencies.Packages.Select(PackagingUtility.GetLibraryDependencyFromNuspec));
            }

            return libraryDependencies;
        }

        private async Task EnsureResource()
        {
            if (_findPackagesByIdResource == null)
            {
                var resource = await _sourceRepository.GetResourceAsync<FindPackageByIdResource>();

                lock (_lock)
                {
                    if (_findPackagesByIdResource == null)
                    {
                        _findPackagesByIdResource = resource;
                    }
                }
            }
        }

        /// <summary>
        /// Retrieve a package from the feed and read the original identity from the nuspec.
        /// </summary>
        private async Task<LibraryIdentity> GetLibraryIdentityAsync(
            LibraryRange libraryRange,
            NuGetFramework targetFramework,
            SourceCacheContext cacheContext,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // Discover all versions from the feed
            var packageVersions = await GetPackageVersions(libraryRange.Name, cacheContext, logger, cancellationToken);

            // Select the best match
            var packageVersion = packageVersions?.FindBestMatch(libraryRange.VersionRange, version => version);

            if (packageVersion != null)
            {
                // Use the original package identity for the library identity
                var originalIdentity = await _findPackagesByIdResource.GetOriginalIdentityAsync(
                                        libraryRange.Name,
                                        packageVersion,
                                        cacheContext,
                                        logger,
                                        cancellationToken);

                return new LibraryIdentity
                {
                    Name = originalIdentity.Id,
                    Version = originalIdentity.Version,
                    Type = LibraryType.Package
                };
            }

            return null;
        }

        /// <summary>
        /// Discover all package versions from a feed.
        /// </summary>
        private async Task<IEnumerable<NuGetVersion>> GetPackageVersions(string id,
                                                                    SourceCacheContext cacheContext,
                                                                    ILogger logger,
                                                                    CancellationToken cancellationToken)
        {
            IEnumerable<NuGetVersion> packageVersions = null;
            try
            {
                if (_throttle != null)
                {
                    await _throttle.WaitAsync();
                }
                packageVersions = await _findPackagesByIdResource.GetAllVersionsAsync(
                    id,
                    cacheContext,
                    logger,
                    cancellationToken);
            }
            catch (FatalProtocolException e) when (_ignoreFailedSources)
            {
                if (!_ignoreWarning)
                {
                    _logger.LogWarning(e.Message);
                }
                return null;
            }
            finally
            {
                _throttle?.Release();
            }

            return packageVersions;
        }
    }
}
