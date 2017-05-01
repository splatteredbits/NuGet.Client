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
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace NuGet.Packaging
{
    /// <summary>
    /// Abstract class that both the zip and folder package readers extend
    /// This class contains the path conventions for both zip and folder readers
    /// </summary>
    public abstract class PackageReaderBase : IPackageCoreReader, IPackageContentReader
    {
        private NuspecReader _nuspecReader;

        protected IFrameworkNameProvider FrameworkProvider { get; set; }
        protected IFrameworkCompatibilityProvider CompatibilityProvider { get; set; }

        /// <summary>
        /// Core package reader
        /// </summary>
        /// <param name="frameworkProvider">framework mapping provider</param>
        public PackageReaderBase(IFrameworkNameProvider frameworkProvider)
            : this(frameworkProvider, new CompatibilityProvider(frameworkProvider))
        {
        }

        /// <summary>
        /// Core package reader
        /// </summary>
        /// <param name="frameworkProvider">framework mapping provider</param>
        /// <param name="compatibilityProvider">framework compatibility provider</param>
        public PackageReaderBase(IFrameworkNameProvider frameworkProvider, IFrameworkCompatibilityProvider compatibilityProvider)
            : base()
        {
            if (frameworkProvider == null)
            {
                throw new ArgumentNullException(nameof(frameworkProvider));
            }

            if (compatibilityProvider == null)
            {
                throw new ArgumentNullException(nameof(compatibilityProvider));
            }

            FrameworkProvider = frameworkProvider;
            CompatibilityProvider = compatibilityProvider;
        }

        #region IPackageCoreReader implementation

        public abstract Stream GetStream(string path);

        public virtual Task<Stream> GetStreamAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetStream(path));
        }

        public abstract IEnumerable<string> GetFiles();

        public virtual Task<IEnumerable<string>> GetFilesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetFiles());
        }

        public abstract IEnumerable<string> GetFiles(string folder);

        public virtual Task<IEnumerable<string>> GetFilesAsync(string folder, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetFiles(folder));
        }

        public abstract IEnumerable<string> CopyFiles(
            string destination,
            IEnumerable<string> packageFiles,
            ExtractPackageFileDelegate extractFile,
            ILogger logger,
            CancellationToken token);

        public virtual Task<IEnumerable<string>> CopyFilesAsync(
            string destination,
            IEnumerable<string> packageFiles,
            ExtractPackageFileDelegate extractFile,
            ILogger logger,
            CancellationToken token)
        {
            return Task.FromResult(CopyFiles(destination, packageFiles, extractFile, logger, token));
        }

        public virtual PackageIdentity GetIdentity()
        {
            return NuspecReader.GetIdentity();
        }

        public virtual Task<PackageIdentity> GetIdentityAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(NuspecReader.GetIdentity());
        }

        public virtual NuGetVersion GetMinClientVersion()
        {
            return NuspecReader.GetMinClientVersion();
        }

        public virtual Task<NuGetVersion> GetMinClientVersionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetMinClientVersion());
        }

        public virtual IReadOnlyList<PackageType> GetPackageTypes()
        {
            return NuspecReader.GetPackageTypes();
        }

        public virtual Task<IReadOnlyList<PackageType>> GetPackageTypesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetPackageTypes());
        }

        public virtual Stream GetNuspec()
        {
            // This is the default implementation. It is overridden and optimized in
            // PackageArchiveReader and PackageFolderReader.
            return GetStream(GetNuspecFile());
        }

        public virtual Task<Stream> GetNuspecAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetNuspec());
        }

        public virtual string GetNuspecFile()
        {
            var files = GetFiles();

            return GetNuspecFile(files);
        }

        public virtual Task<string> GetNuspecFileAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetNuspecFile());
        }

        /// <summary>
        /// Nuspec reader
        /// </summary>
        public virtual NuspecReader NuspecReader
        {
            get
            {
                if (_nuspecReader == null)
                {
                    _nuspecReader = new NuspecReader(GetNuspec());
                }

                return _nuspecReader;
            }
        }

        public virtual Task<NuspecReader> GetNuspecReaderAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(NuspecReader);
        }

        #endregion

        #region IDisposable implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected abstract void Dispose(bool disposing);

        #endregion

        #region IPackageContentReader implementation

        /// <summary>
        /// Frameworks mentioned in the package.
        /// </summary>
        public virtual IEnumerable<NuGetFramework> GetSupportedFrameworks()
        {
            var frameworks = new HashSet<NuGetFramework>(new NuGetFrameworkFullComparer());

            frameworks.UnionWith(GetLibItems().Select(g => g.TargetFramework));

            frameworks.UnionWith(GetBuildItems().Select(g => g.TargetFramework));

            frameworks.UnionWith(GetContentItems().Select(g => g.TargetFramework));

            frameworks.UnionWith(GetToolItems().Select(g => g.TargetFramework));

            frameworks.UnionWith(GetFrameworkItems().Select(g => g.TargetFramework));

            return frameworks.Where(f => !f.IsUnsupported).OrderBy(f => f, new NuGetFrameworkSorter());
        }

        public virtual Task<IEnumerable<NuGetFramework>> GetSupportedFrameworksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetSupportedFrameworks());
        }

        public virtual IEnumerable<FrameworkSpecificGroup> GetFrameworkItems()
        {
            return NuspecReader.GetFrameworkReferenceGroups();
        }

        public virtual Task<IEnumerable<FrameworkSpecificGroup>> GetFrameworkItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(NuspecReader.GetFrameworkReferenceGroups());
        }

        public virtual bool IsServiceable()
        {
            return NuspecReader.IsServiceable();
        }

        public virtual Task<bool> IsServiceableAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(NuspecReader.IsServiceable());
        }

        public virtual IEnumerable<FrameworkSpecificGroup> GetBuildItems()
        {
            var id = GetIdentity().Id;

            var results = new List<FrameworkSpecificGroup>();

            foreach (var group in GetFileGroups(PackagingConstants.Folders.Build))
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

        public virtual Task<IEnumerable<FrameworkSpecificGroup>> GetBuildItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetBuildItems());
        }

        /// <summary>
        /// only packageId.targets and packageId.props should be used from the build folder
        /// </summary>
        protected static bool IsAllowedBuildFile(string packageId, string path)
        {
            var file = Path.GetFileName(path);

            return StringComparer.OrdinalIgnoreCase.Equals(file, string.Format(CultureInfo.InvariantCulture, "{0}.targets", packageId))
                   || StringComparer.OrdinalIgnoreCase.Equals(file, string.Format(CultureInfo.InvariantCulture, "{0}.props", packageId));
        }

        public virtual IEnumerable<FrameworkSpecificGroup> GetToolItems()
        {
            return GetFileGroups(PackagingConstants.Folders.Tools);
        }

        public virtual Task<IEnumerable<FrameworkSpecificGroup>> GetToolItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetFileGroups(PackagingConstants.Folders.Tools));
        }

        public virtual IEnumerable<FrameworkSpecificGroup> GetContentItems()
        {
            return GetFileGroups(PackagingConstants.Folders.Content);
        }

        public virtual Task<IEnumerable<FrameworkSpecificGroup>> GetContentItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetFileGroups(PackagingConstants.Folders.Content));
        }

        public virtual IEnumerable<FrameworkSpecificGroup> GetContentItems(string contentFolderName)
        {
            return GetFileGroups(contentFolderName);
        }

        public virtual Task<IEnumerable<FrameworkSpecificGroup>> GetContentItemsAsync(string contentFolderName, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetFileGroups(contentFolderName));
        }

        public virtual IEnumerable<PackageDependencyGroup> GetPackageDependencies()
        {
            return NuspecReader.GetDependencyGroups();
        }

        public virtual Task<IEnumerable<PackageDependencyGroup>> GetPackageDependencieAsyncs(CancellationToken cancellationToken)
        {
            return Task.FromResult(NuspecReader.GetDependencyGroups());
        }

        public virtual IEnumerable<FrameworkSpecificGroup> GetLibItems()
        {
            return GetFileGroups(PackagingConstants.Folders.Lib);
        }

        public virtual Task<IEnumerable<FrameworkSpecificGroup>> GetLibItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetFileGroups(PackagingConstants.Folders.Lib));
        }

        public virtual IEnumerable<FrameworkSpecificGroup> GetLibItems(string libFolderName)
        {
            return GetFileGroups(libFolderName);
        }

        public virtual Task<IEnumerable<FrameworkSpecificGroup>> GetLibItemsAsync(string libFolderName, CancellationToken cancellationToken)
        {
            return Task.FromResult(GetFileGroups(libFolderName));
        }

        /// <summary>
        /// True only for assemblies that should be added as references to msbuild projects
        /// </summary>
        protected static bool IsReferenceAssembly(string path)
        {
            var result = false;

            var extension = Path.GetExtension(path);

            if (StringComparer.OrdinalIgnoreCase.Equals(extension, ".dll"))
            {
                if (!path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                }
            }
            else if (StringComparer.OrdinalIgnoreCase.Equals(extension, ".winmd"))
            {
                result = true;
            }
            else if (StringComparer.OrdinalIgnoreCase.Equals(extension, ".exe"))
            {
                result = true;
            }

            return result;
        }

        public virtual IEnumerable<FrameworkSpecificGroup> GetReferenceItems()
        {
            var referenceGroups = NuspecReader.GetReferenceGroups();
            var fileGroups = new List<FrameworkSpecificGroup>();

            // filter out non reference assemblies
            foreach (var group in GetLibItems())
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

        public virtual Task<IEnumerable<FrameworkSpecificGroup>> GetReferenceItemsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetReferenceItems());
        }

        public virtual bool GetDevelopmentDependency()
        {
            return NuspecReader.GetDevelopmentDependency();
        }

        public virtual Task<bool> GetDevelopmentDependencyAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(GetDevelopmentDependency());
        }

        protected IEnumerable<FrameworkSpecificGroup> GetFileGroups(string folder)
        {
            var groups = new Dictionary<NuGetFramework, List<string>>(new NuGetFrameworkFullComparer());

            var isContentFolder = StringComparer.OrdinalIgnoreCase.Equals(folder, PackagingConstants.Folders.Content);
            var allowSubFolders = true;

            foreach (var path in GetFiles(folder))
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
            foreach (var framework in groups.Keys.OrderBy(e => e, new NuGetFrameworkSorter()))
            {
                yield return new FrameworkSpecificGroup(framework, groups[framework].OrderBy(e => e, StringComparer.OrdinalIgnoreCase));
            }
        }

        protected NuGetFramework GetFrameworkFromPath(string path, bool allowSubFolders = false)
        {
            var framework = NuGetFramework.AnyFramework;

            var parts = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            // ignore paths that are too short, and ones that have additional sub directories
            if (parts.Length == 3
                || (parts.Length > 3 && allowSubFolders))
            {
                var folderName = parts[1];

                NuGetFramework parsedFramework;
                try
                {
                    parsedFramework = NuGetFramework.ParseFolder(folderName, FrameworkProvider);
                }
                catch (ArgumentException e)
                {
                    // Include package name context in the exception.
                    throw new PackagingException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.InvalidPackageFrameworkFolderName,
                            path,
                            GetIdentity()),
                        e);
                }

                if (parsedFramework.IsSpecificFramework)
                {
                    // the folder name is a known target framework
                    framework = parsedFramework;
                }
            }

            return framework;
        }

        #endregion

        protected static string GetNuspecFile(IEnumerable<string> files)
        {
            // Find all nuspecs in the root folder.
            var nuspecPaths = files
                .Where(entryPath => PackageHelper.IsManifest(entryPath))
                .ToList();

            if (nuspecPaths.Count == 0)
            {
                throw new PackagingException(Strings.MissingNuspec);
            }
            else if (nuspecPaths.Count > 1)
            {
                throw new PackagingException(Strings.MultipleNuspecFiles);
            }

            return nuspecPaths.Single();
        }
    }
}