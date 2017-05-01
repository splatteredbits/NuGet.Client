// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace NuGet.Protocol.Plugins
{
    /// <summary>
    /// Delegates package read operations to a plugin.
    /// </summary>
    public sealed class PluginPackageReader : PackageReaderBase
    {
        private readonly SemaphoreSlim _getFilesSemaphore;
        private readonly SemaphoreSlim _getNuspecReaderSemaphore;
        private readonly ConcurrentDictionary<string, Lazy<Task<FileStreamCreator>>> _fileStreams;
        private IEnumerable<string> _files;
        private bool _isDisposed;
        private NuspecReader _nuspecReader;
        private readonly PackageIdentity _packageIdentity;
        private readonly string _packageSourceRepository;
        private readonly IPlugin _plugin;

        public PluginPackageReader(IPlugin plugin, PackageIdentity packageIdentity, string packageSourceRepository)
            : base(DefaultFrameworkNameProvider.Instance, DefaultCompatibilityProvider.Instance)
        {
            if (plugin == null)
            {
                throw new ArgumentNullException(nameof(plugin));
            }

            if (string.IsNullOrEmpty(packageSourceRepository))
            {
                throw new ArgumentException(Strings.ArgumentCannotBeNullOrEmpty, nameof(packageSourceRepository));
            }

            _plugin = plugin;
            _packageIdentity = packageIdentity;
            _packageSourceRepository = packageSourceRepository;
            _getFilesSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
            _getNuspecReaderSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
            _fileStreams = new ConcurrentDictionary<string, Lazy<Task<FileStreamCreator>>>(StringComparer.OrdinalIgnoreCase);
        }

        public override Stream GetStream(string path)
        {
            throw new NotSupportedException();
        }

        public override async Task<Stream> GetStreamAsync(string path, CancellationToken cancellationToken)
        {
            var lazyCreator = _fileStreams.GetOrAdd(path, (p) => new Lazy<Task<FileStreamCreator>>(
                () => GetStreamInternalAsync(p, cancellationToken)));

            await lazyCreator.Value;

            return lazyCreator.Value.Result.Create();
        }

        public override IEnumerable<string> GetFiles()
        {
            throw new NotSupportedException();
        }

        public override async Task<IEnumerable<string>> GetFilesAsync(CancellationToken cancellationToken)
        {
            if (_files != null)
            {
                return _files;
            }

            await _getFilesSemaphore.WaitAsync(cancellationToken);

            try
            {
                _files = await GetFilesInternalAsync(cancellationToken);
            }
            finally
            {
                _getFilesSemaphore.Release();
            }

            return _files;
        }

        public override IEnumerable<string> GetFiles(string folder)
        {
            throw new NotSupportedException();
        }

        public override async Task<IEnumerable<string>> GetFilesAsync(string folder, CancellationToken cancellationToken)
        {
            var files = await GetFilesAsync(cancellationToken);

            return files.Where(f => f.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
        }

        public override IEnumerable<string> CopyFiles(
            string destination,
            IEnumerable<string> packageFiles,
            ExtractPackageFileDelegate extractFile,
            ILogger logger,
            CancellationToken token)
        {
            throw new NotSupportedException();
        }

        public override async Task<IEnumerable<string>> CopyFilesAsync(
            string destination,
            IEnumerable<string> packageFiles,
            ExtractPackageFileDelegate extractFile,
            ILogger logger,
            CancellationToken token)
        {
            var packageId = _packageIdentity.Id;
            var packageVersion = _packageIdentity.Version.ToNormalizedString();
            var request = new CopyPackageFilesRequest(
                _packageSourceRepository,
                packageId,
                packageVersion,
                packageFiles,
                destination);
            var response = await _plugin.Connection.SendRequestAndReceiveResponseAsync<CopyPackageFilesRequest, CopyPackageFilesResponse>(
                MessageMethod.CopyPackageFiles,
                request,
                token);

            if (response.ResponseCode == MessageResponseCode.Success)
            {
                return response.CopiedFiles;
            }

            throw new PluginException($"Failed to get files for {packageId}.{packageVersion}.");
        }

        public override PackageIdentity GetIdentity()
        {
            throw new NotSupportedException();
        }

        public override async Task<PackageIdentity> GetIdentityAsync(CancellationToken cancellationToken)
        {
            var nuspecReader = await GetNuspecReaderAsync(cancellationToken);

            return nuspecReader.GetIdentity();
        }

        public override NuGetVersion GetMinClientVersion()
        {
            throw new NotSupportedException();
        }

        public override async Task<NuGetVersion> GetMinClientVersionAsync(CancellationToken cancellationToken)
        {
            var nuspecReader = await GetNuspecReaderAsync(cancellationToken);

            return nuspecReader.GetMinClientVersion();
        }

        public override IReadOnlyList<PackageType> GetPackageTypes()
        {
            throw new NotSupportedException();
        }

        public override async Task<IReadOnlyList<PackageType>> GetPackageTypesAsync(CancellationToken cancellationToken)
        {
            var nuspecReader = await GetNuspecReaderAsync(cancellationToken);

            return nuspecReader.GetPackageTypes();
        }

        public override Stream GetNuspec()
        {
            throw new NotSupportedException();
        }

        public override async Task<Stream> GetNuspecAsync(CancellationToken cancellationToken)
        {
            var nuspecFile = await GetNuspecFileAsync(cancellationToken);

            return await GetStreamAsync(nuspecFile, cancellationToken);
        }

        public override string GetNuspecFile()
        {
            throw new NotSupportedException();
        }

        public override async Task<string> GetNuspecFileAsync(CancellationToken cancellationToken)
        {
            var files = await GetFilesAsync(cancellationToken);

            return GetNuspecFile(files);
        }

        public override NuspecReader NuspecReader => throw new NotSupportedException();

        public override async Task<NuspecReader> GetNuspecReaderAsync(CancellationToken cancellationToken)
        {
            if (_nuspecReader != null)
            {
                return _nuspecReader;
            }

            await _getNuspecReaderSemaphore.WaitAsync(cancellationToken);

            try
            {
                var stream = await GetNuspecAsync(cancellationToken);

                _nuspecReader = new NuspecReader(stream);
            }
            finally
            {
                _getNuspecReaderSemaphore.Release();
            }

            return _nuspecReader;
        }

        public override IEnumerable<NuGetFramework> GetSupportedFrameworks()
        {
            throw new NotSupportedException();
        }

        public override async Task<IEnumerable<NuGetFramework>> GetSupportedFrameworksAsync(CancellationToken cancellationToken)
        {
            var frameworks = new HashSet<NuGetFramework>(new NuGetFrameworkFullComparer());

            frameworks.UnionWith((await GetLibItemsAsync(cancellationToken)).Select(g => g.TargetFramework));

            frameworks.UnionWith((await GetBuildItemsAsync(cancellationToken)).Select(g => g.TargetFramework));

            frameworks.UnionWith((await GetContentItemsAsync(cancellationToken)).Select(g => g.TargetFramework));

            frameworks.UnionWith((await GetToolItemsAsync(cancellationToken)).Select(g => g.TargetFramework));

            frameworks.UnionWith((await GetFrameworkItemsAsync(cancellationToken)).Select(g => g.TargetFramework));

            return frameworks.Where(f => !f.IsUnsupported).OrderBy(f => f, new NuGetFrameworkSorter());
        }

        public override IEnumerable<FrameworkSpecificGroup> GetFrameworkItems()
        {
            throw new NotSupportedException();
        }

        public override async Task<IEnumerable<FrameworkSpecificGroup>> GetFrameworkItemsAsync(CancellationToken cancellationToken)
        {
            var nuspecReader = await GetNuspecReaderAsync(cancellationToken);

            return nuspecReader.GetFrameworkReferenceGroups();
        }

        public override bool IsServiceable()
        {
            throw new NotSupportedException();
        }

        public override async Task<bool> IsServiceableAsync(CancellationToken cancellationToken)
        {
            var nuspecReader = await GetNuspecReaderAsync(cancellationToken);

            return nuspecReader.IsServiceable();
        }

        public override IEnumerable<FrameworkSpecificGroup> GetBuildItems()
        {
            throw new NotSupportedException();
        }

        public override async Task<IEnumerable<FrameworkSpecificGroup>> GetBuildItemsAsync(CancellationToken cancellationToken)
        {
            var nuspecReader = await GetNuspecReaderAsync(cancellationToken);
            var id = nuspecReader.GetIdentity().Id;

            var results = new List<FrameworkSpecificGroup>();

            foreach (var group in await GetFileGroupsAsync(PackagingConstants.Folders.Build, cancellationToken))
            {
                var filteredGroup = group;

                if (group.Items.Any(e => !IsAllowedBuildFile(id, e)))
                {
                    // create a new group with only valid files
                    filteredGroup = new FrameworkSpecificGroup(group.TargetFramework, group.Items.Where(e => IsAllowedBuildFile(id, e)));

                    if (!filteredGroup.Items.Any())
                    {
                        // nothing was useful in the folder, skip this group completely
                        filteredGroup = null;
                    }
                }

                if (filteredGroup != null)
                {
                    results.Add(filteredGroup);
                }
            }

            return results;
        }

        public override IEnumerable<FrameworkSpecificGroup> GetToolItems()
        {
            throw new NotSupportedException();
        }

        public override Task<IEnumerable<FrameworkSpecificGroup>> GetToolItemsAsync(CancellationToken cancellationToken)
        {
            return GetFileGroupsAsync(PackagingConstants.Folders.Tools, cancellationToken);
        }

        public override IEnumerable<FrameworkSpecificGroup> GetContentItems()
        {
            throw new NotSupportedException();
        }

        public override Task<IEnumerable<FrameworkSpecificGroup>> GetContentItemsAsync(CancellationToken cancellationToken)
        {
            return GetFileGroupsAsync(PackagingConstants.Folders.Content, cancellationToken);
        }

        public override IEnumerable<FrameworkSpecificGroup> GetContentItems(string contentFolderName)
        {
            throw new NotSupportedException();
        }

        public override Task<IEnumerable<FrameworkSpecificGroup>> GetContentItemsAsync(string contentFolderName, CancellationToken cancellationToken)
        {
            return GetFileGroupsAsync(contentFolderName, cancellationToken);
        }

        public override IEnumerable<PackageDependencyGroup> GetPackageDependencies()
        {
            throw new NotSupportedException();
        }

        public override async Task<IEnumerable<PackageDependencyGroup>> GetPackageDependencieAsyncs(CancellationToken cancellationToken)
        {
            var nuspecReader = await GetNuspecReaderAsync(cancellationToken);

            return nuspecReader.GetDependencyGroups();
        }

        public override IEnumerable<FrameworkSpecificGroup> GetLibItems()
        {
            throw new NotSupportedException();
        }

        public override Task<IEnumerable<FrameworkSpecificGroup>> GetLibItemsAsync(CancellationToken cancellationToken)
        {
            return GetFileGroupsAsync(PackagingConstants.Folders.Lib, cancellationToken);
        }

        public override IEnumerable<FrameworkSpecificGroup> GetLibItems(string libFolderName)
        {
            throw new NotSupportedException();
        }

        public override Task<IEnumerable<FrameworkSpecificGroup>> GetLibItemsAsync(string libFolderName, CancellationToken cancellationToken)
        {
            return GetFileGroupsAsync(libFolderName, cancellationToken);
        }

        public override IEnumerable<FrameworkSpecificGroup> GetReferenceItems()
        {
            throw new NotSupportedException();
        }

        public override async Task<IEnumerable<FrameworkSpecificGroup>> GetReferenceItemsAsync(CancellationToken cancellationToken)
        {
            var nuspecReader = await GetNuspecReaderAsync(cancellationToken);
            var referenceGroups = nuspecReader.GetReferenceGroups();
            var fileGroups = new List<FrameworkSpecificGroup>();

            // filter out non reference assemblies
            foreach (var group in await GetLibItemsAsync(cancellationToken))
            {
                fileGroups.Add(new FrameworkSpecificGroup(group.TargetFramework, group.Items.Where(e => IsReferenceAssembly(e))));
            }

            // results
            var libItems = new List<FrameworkSpecificGroup>();

            if (referenceGroups.Any())
            {
                // the 'any' group from references, for pre2.5 nuspecs this will be the only group
                var fallbackGroup = referenceGroups.Where(g => g.TargetFramework.Equals(NuGetFramework.AnyFramework)).FirstOrDefault();

                foreach (var fileGroup in fileGroups)
                {
                    // check for a matching reference group to use for filtering
                    var referenceGroup = NuGetFrameworkUtility.GetNearest<FrameworkSpecificGroup>(
                                                                           items: referenceGroups,
                                                                           framework: fileGroup.TargetFramework,
                                                                           frameworkMappings: FrameworkProvider,
                                                                           compatibilityProvider: CompatibilityProvider);

                    if (referenceGroup == null)
                    {
                        referenceGroup = fallbackGroup;
                    }

                    if (referenceGroup == null)
                    {
                        // add the lib items without any filtering
                        libItems.Add(fileGroup);
                    }
                    else
                    {
                        var filteredItems = new List<string>();

                        foreach (var path in fileGroup.Items)
                        {
                            // reference groups only have the file name, not the path
                            var file = Path.GetFileName(path);

                            if (referenceGroup.Items.Any(s => StringComparer.OrdinalIgnoreCase.Equals(s, file)))
                            {
                                filteredItems.Add(path);
                            }
                        }

                        if (filteredItems.Any())
                        {
                            libItems.Add(new FrameworkSpecificGroup(fileGroup.TargetFramework, filteredItems));
                        }
                    }
                }
            }
            else
            {
                libItems.AddRange(fileGroups);
            }

            return libItems;
        }

        public override bool GetDevelopmentDependency()
        {
            throw new NotSupportedException();
        }

        public override async Task<bool> GetDevelopmentDependencyAsync(CancellationToken cancellationToken)
        {
            var nuspecReader = await GetNuspecReaderAsync(cancellationToken);

            return nuspecReader.GetDevelopmentDependency();
        }

        private async Task<IEnumerable<FrameworkSpecificGroup>> GetFileGroupsAsync(string folder, CancellationToken cancellationToken)
        {
            var groups = new Dictionary<NuGetFramework, List<string>>(new NuGetFrameworkFullComparer());

            var isContentFolder = StringComparer.OrdinalIgnoreCase.Equals(folder, PackagingConstants.Folders.Content);
            var allowSubFolders = true;

            foreach (var path in await GetFilesAsync(folder, cancellationToken))
            {
                // Use the known framework or if the folder did not parse, use the Any framework and consider it a sub folder
                var framework = GetFrameworkFromPath(path, allowSubFolders);

                List<string> items = null;
                if (!groups.TryGetValue(framework, out items))
                {
                    items = new List<string>();
                    groups.Add(framework, items);
                }

                items.Add(path);
            }

            // Sort the groups by framework, and the items by ordinal string compare to keep things deterministic
            return groups.Keys.OrderBy(e => e, new NuGetFrameworkSorter())
                .Select(framework => new FrameworkSpecificGroup(framework, groups[framework].OrderBy(e => e, StringComparer.OrdinalIgnoreCase)));
        }

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                foreach (var pair in _fileStreams)
                {
                    if (pair.Value.Value.Status == TaskStatus.RanToCompletion)
                    {
                        var fileStream = pair.Value.Value.Result;

                        fileStream.Dispose();
                    }
                }

                _fileStreams.Clear();

                _plugin.Dispose();

                _getFilesSemaphore.Dispose();
                _getNuspecReaderSemaphore.Dispose();

                _isDisposed = true;
            }
        }

        private async Task<FileStreamCreator> GetStreamInternalAsync(string pathInPackage, CancellationToken cancellationToken)
        {
            var packageId = _packageIdentity.Id;
            var packageVersion = _packageIdentity.Version.ToNormalizedString();
            var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var payload = new GetFileInPackageRequest(
                _packageSourceRepository,
                packageId,
                packageVersion,
                pathInPackage,
                tempFilePath);

            var response = await _plugin.Connection.SendRequestAndReceiveResponseAsync<GetFileInPackageRequest, GetFileInPackageResponse>(
                MessageMethod.GetFileInPackage,
                payload,
                CancellationToken.None);

            if (response.ResponseCode == MessageResponseCode.Success)
            {
                return new FileStreamCreator(tempFilePath);
            }
            else
            {
                throw new PluginException($"Plugin failed to download {pathInPackage}.");
            }
        }

        private async Task<IEnumerable<string>> GetFilesInternalAsync(CancellationToken cancellationToken)
        {
            var packageId = _packageIdentity.Id;
            var packageVersion = _packageIdentity.Version.ToNormalizedString();
            var request = new GetFilesInPackageRequest(_packageSourceRepository, packageId, packageVersion);
            var response = await _plugin.Connection.SendRequestAndReceiveResponseAsync<GetFilesInPackageRequest, GetFilesInPackageResponse>(
                MessageMethod.GetFilesInPackage,
                request,
                cancellationToken);

            if (response.ResponseCode == MessageResponseCode.Success)
            {
                return response.Files;
            }

            throw new PluginException($"Failed to get files for {packageId}.{packageVersion}.");
        }

        private sealed class FileStreamCreator : IDisposable
        {
            private readonly string _filePath;
            private bool _isDisposed;

            internal FileStreamCreator(string filePath)
            {
                _filePath = filePath;
            }

            public void Dispose()
            {
                if (!_isDisposed)
                {
                    try
                    {
                        File.Delete(_filePath);
                    }
                    catch (Exception)
                    {
                    }

                    GC.SuppressFinalize(this);

                    _isDisposed = true;
                }
            }

            internal FileStream Create()
            {
                return new FileStream(
                     _filePath,
                     FileMode.Open,
                     FileAccess.Read,
                     FileShare.Read);
            }
        }
    }
}