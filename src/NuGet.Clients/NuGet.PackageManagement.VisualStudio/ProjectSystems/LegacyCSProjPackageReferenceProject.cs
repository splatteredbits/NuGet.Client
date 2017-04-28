// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using NuGet.Commands;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.ProjectManagement.Projects;
using NuGet.ProjectModel;
using NuGet.RuntimeModel;
using NuGet.Versioning;
using NuGet.VisualStudio;
using VSLangProj150;

namespace NuGet.PackageManagement.VisualStudio
{
    /// <summary>
    /// An implementation of <see cref="NuGetProject"/> that interfaces with VS project APIs to coordinate
    /// packages in a legacy CSProj with package references.
    /// </summary>
    public class LegacyCSProjPackageReferenceProject : BuildIntegratedNuGetProject
    {
        private const string IncludeAssets = "IncludeAssets";
        private const string ExcludeAssets = "ExcludeAssets";
        private const string PrivateAssets = "PrivateAssets";

        private static Array _desiredPackageReferenceMetadata;

        private readonly IVsProjectAdapter _project;
        private readonly Lazy<VSProject4> _asVSProject4;

        private IScriptExecutor _scriptExecutor;
        private string _projectName;
        private string _projectUniqueName;
        private string _projectFullPath;
        private bool _callerIsUnitTest;

        static LegacyCSProjPackageReferenceProject()
        {
            _desiredPackageReferenceMetadata = Array.CreateInstance(typeof(string), 3);
            _desiredPackageReferenceMetadata.SetValue(IncludeAssets, 0);
            _desiredPackageReferenceMetadata.SetValue(ExcludeAssets, 1);
            _desiredPackageReferenceMetadata.SetValue(PrivateAssets, 2);
        }

        public LegacyCSProjPackageReferenceProject(
            IVsProjectAdapter project,
            string projectId,
            bool callerIsUnitTest = false)
        {
            Assumes.Present(project);

            _project = project;
            _asVSProject4 = new Lazy<VSProject4>(() => _project.Project.Object as VSProject4);

            _projectName = _project.ProjectName;
            _projectUniqueName = _project.UniqueName;
            _projectFullPath = _project.FullPath;
            _callerIsUnitTest = callerIsUnitTest;

            InternalMetadata.Add(NuGetProjectMetadataKeys.Name, _projectName);
            InternalMetadata.Add(NuGetProjectMetadataKeys.UniqueName, _projectUniqueName);
            InternalMetadata.Add(NuGetProjectMetadataKeys.FullPath, _projectFullPath);
            InternalMetadata.Add(NuGetProjectMetadataKeys.ProjectId, projectId);
        }

        public override string ProjectName => _projectName;

        private VSProject4 AsVSProject4 => _asVSProject4.Value;

        private IScriptExecutor ScriptExecutor
        {
            get
            {
                if (_scriptExecutor == null)
                {
                    _scriptExecutor = ServiceLocator.GetInstanceSafe<IScriptExecutor>();
                }

                return _scriptExecutor;
            }
        }

        public override async Task<string> GetAssetsFilePathAsync()
        {
            return await GetAssetsFilePathAsync(shouldThrow: true);
        }

        public override async Task<string> GetAssetsFilePathOrNullAsync()
        {
            return await GetAssetsFilePathAsync(shouldThrow: false);
        }

        private async Task<string> GetAssetsFilePathAsync(bool shouldThrow)
        {
            var baseIntermediatePath = await GetBaseIntermediatePathAsync(shouldThrow);

            if (baseIntermediatePath == null)
            {
                return null;
            }

            return Path.Combine(baseIntermediatePath, LockFileFormat.AssetsFileName);
        }

        public override async Task<bool> ExecuteInitScriptAsync(
            PackageIdentity identity,
            string packageInstallPath,
            INuGetProjectContext projectContext,
            bool throwOnFailure)
        {
            return
                await
                    ScriptExecutorUtil.ExecuteScriptAsync(identity, packageInstallPath, projectContext, ScriptExecutor,
                        _project.Project, throwOnFailure);
        }

        #region IDependencyGraphProject

        public override string MSBuildProjectPath => _projectFullPath;

        public override async Task<IReadOnlyList<PackageSpec>> GetPackageSpecsAsync(DependencyGraphCacheContext context)
        {
            PackageSpec packageSpec;
            if (context == null || !context.PackageSpecCache.TryGetValue(MSBuildProjectPath, out packageSpec))
            {
                packageSpec = await GetPackageSpecAsync();
                if (packageSpec == null)
                {
                    throw new InvalidOperationException(
                        string.Format(Strings.ProjectNotLoaded_RestoreFailed, ProjectName));
                }
                context?.PackageSpecCache.Add(_projectFullPath, packageSpec);
            }

            return new[] { packageSpec };
        }

        #endregion

        #region NuGetProject

        public override async Task<IEnumerable<PackageReference>> GetInstalledPackagesAsync(CancellationToken token)
        {
            return GetPackageReferences(await GetPackageSpecAsync());
        }

        public override async Task<bool> InstallPackageAsync(
            string packageId,
            VersionRange range,
            INuGetProjectContext nuGetProjectContext,
            BuildIntegratedInstallationContext installationContext,
            CancellationToken token)
        {
            return await InstallPackageWithMetadataAsync(packageId,
                range,
                metadataElements: new string[0],
                metadataValues: new string[0]);
        }

        public async Task<bool> InstallPackageWithMetadataAsync(
            string packageId,
            VersionRange range,
            IEnumerable<string> metadataElements,
            IEnumerable<string> metadataValues)
        {
            var success = false;

            await NuGetUIThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // We don't adjust package reference metadata from UI
                AddOrUpdateLegacyCSProjPackage(
                    packageId,
                    range.OriginalString ?? range.ToShortString(),
                    metadataElements?.ToArray() ?? new string[0],
                    metadataValues?.ToArray() ?? new string[0]);

                success = true;
            });

            return success;
        }

        /// <summary>
        /// Add a new package reference or update an existing one on a legacy CSProj project
        /// </summary>
        /// <param name="packageName">Name of package to add or update</param>
        /// <param name="packageVersion">Version of new package/new version of existing package</param>
        /// <param name="metadataElements">Element names of metadata to add to package reference</param>
        /// <param name="metadataValues">Element values of metadata to add to package reference</param>
        private void AddOrUpdateLegacyCSProjPackage(string packageName, string packageVersion, string[] metadataElements, string[] metadataValues)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Note that API behavior is:
            // - specify a metadata element name with a value => add/replace that metadata item on the package reference
            // - specify a metadata element name with no value => remove that metadata item from the project reference
            // - don't specify a particular metadata name => if it exists on the package reference, don't change it (e.g. for user defined metadata)
            AsVSProject4.PackageReferences.AddOrUpdate(packageName, packageVersion, metadataElements, metadataValues);
        }

        public override async Task<bool> UninstallPackageAsync(PackageIdentity packageIdentity, INuGetProjectContext nuGetProjectContext, CancellationToken token)
        {
            var success = false;
            await NuGetUIThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                RemoveLegacyCSProjPackage(packageIdentity.Id);

                success = true;
            });

            return success;
        }

        /// <summary>
        /// Remove a package reference from a legacy CSProj project
        /// </summary>
        /// <param name="packageName">Name of package to remove from project</param>
        private void RemoveLegacyCSProjPackage(string packageName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            AsVSProject4.PackageReferences.Remove(packageName);
        }

        #endregion

        private async Task<string> GetBaseIntermediatePathAsync(bool shouldThrow)
        {
            return await RunOnUIThread(() => GetBaseIntermediatePath(shouldThrow));
        }

        private string GetBaseIntermediatePath(bool shouldThrow = true)
        {
            EnsureUIThread();

            var baseIntermediatePath = _project.BaseIntermediateOutputPath;

            if (string.IsNullOrEmpty(baseIntermediatePath))
            {
                if (shouldThrow)
                {
                    throw new InvalidDataException(nameof(_project.BaseIntermediateOutputPath));
                }
                else
                {
                    return null;
                }
            }

            return baseIntermediatePath;
        }

        private static string[] GetProjectReferences(PackageSpec packageSpec)
        {
            // There is only one target framework for legacy csproj projects
            var targetFramework = packageSpec.TargetFrameworks.FirstOrDefault();
            if (targetFramework == null)
            {
                return new string[] { };
            }

            return targetFramework.Dependencies
                .Where(d => d.LibraryRange.TypeConstraint == LibraryDependencyTarget.ExternalProject)
                .Select(d => d.LibraryRange.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static PackageReference[] GetPackageReferences(PackageSpec packageSpec)
        {
            var frameworkSorter = new NuGetFrameworkSorter();

            return packageSpec
                .TargetFrameworks
                .SelectMany(f => GetPackageReferences(f.Dependencies, f.FrameworkName))
                .GroupBy(p => p.PackageIdentity)
                .Select(g => g.OrderBy(p => p.TargetFramework, frameworkSorter).First())
                .ToArray();
        }

        private static IEnumerable<PackageReference> GetPackageReferences(IEnumerable<LibraryDependency> libraries, NuGetFramework targetFramework)
        {
            return libraries
                .Where(l => l.LibraryRange.TypeConstraint == LibraryDependencyTarget.Package)
                .Select(l => ToPackageReference(l, targetFramework));
        }

        private static PackageReference ToPackageReference(LibraryDependency library, NuGetFramework targetFramework)
        {
            var identity = new PackageIdentity(
                library.LibraryRange.Name,
                library.LibraryRange.VersionRange.MinVersion);

            return new PackageReference(identity, targetFramework);
        }

        private async Task<PackageSpec> GetPackageSpecAsync()
        {
            return await RunOnUIThread(GetPackageSpec);
        }

        /// <summary>
        /// Emulates a JSON deserialization from project.json to PackageSpec in a post-project.json world
        /// </summary>
        private PackageSpec GetPackageSpec()
        {
            EnsureUIThread();

            var projectReferences = GetLegacyCSProjProjectReferences(_desiredPackageReferenceMetadata)
                .Select(ToProjectRestoreReference);

            var packageReferences = GetLegacyCSProjPackageReferences(_desiredPackageReferenceMetadata)
                .Select(ToPackageLibraryDependency).ToList();

            var packageTargetFallback = _project.PackageTargetFallback?.Split(new[] { ';' })
                .Select(NuGetFramework.Parse)
                .ToList();

            var projectTfi = new TargetFrameworkInformation()
            {
                FrameworkName = _project.TargetNuGetFramework,
                Dependencies = packageReferences,
                Imports = packageTargetFallback ?? new List<NuGetFramework>()
            };

            if ((projectTfi.Imports?.Count ?? 0) > 0)
            {
                projectTfi.FrameworkName = new FallbackFramework(projectTfi.FrameworkName, packageTargetFallback);
            }

            // Build up runtime information.
            var runtimes = _project.Runtimes;
            var supports = _project.Supports;
            var runtimeGraph = new RuntimeGraph(runtimes, supports);

            // In legacy CSProj, we only have one target framework per project
            var tfis = new TargetFrameworkInformation[] { projectTfi };

            return new PackageSpec(tfis)
            {
                Name = _projectName ?? _projectUniqueName,
                Version = new NuGetVersion(_project.Version),
                Authors = new string[] { },
                Owners = new string[] { },
                Tags = new string[] { },
                ContentFiles = new string[] { },
                Dependencies = packageReferences,
                FilePath = _projectFullPath,
                RuntimeGraph = runtimeGraph,
                RestoreMetadata = new ProjectRestoreMetadata
                {
                    ProjectStyle = ProjectStyle.PackageReference,
                    OutputPath = GetBaseIntermediatePath(),
                    ProjectPath = _projectFullPath,
                    ProjectName = _projectName ?? _projectUniqueName,
                    ProjectUniqueName = _projectFullPath,
                    OriginalTargetFrameworks = tfis
                        .Select(tfi => tfi.FrameworkName.GetShortFolderName())
                        .ToList(),
                    TargetFrameworks = new List<ProjectRestoreMetadataFrameworkInfo>()
                    {
                        new ProjectRestoreMetadataFrameworkInfo(tfis[0].FrameworkName)
                        {
                            ProjectReferences = projectReferences?.ToList()
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Project references for legacy CSProj project
        /// </summary>
        /// <param name="desiredMetadata">metadata element names requested in returned objects</param>
        /// <returns>An array of returned data for each project reference discovered</returns>
        private IEnumerable<LegacyCSProjProjectReference> GetLegacyCSProjProjectReferences(Array desiredMetadata)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            foreach (var reference in AsVSProject4.References.Cast<Reference6>().Where(r => r.SourceProject != null))
            {
                Array metadataElements;
                Array metadataValues;
                reference.GetMetadata(desiredMetadata, out metadataElements, out metadataValues);

                yield return new LegacyCSProjProjectReference(
                    uniqueName: reference.SourceProject.FullName,
                    metadataElements: metadataElements,
                    metadataValues: metadataValues);
            }
        }

        /// <summary>
        /// Package references for legacy CSProj project
        /// </summary>
        /// <param name="desiredMetadata">metadata element names requested in returned objects</param>
        /// <returns>An array of returned data for each package reference discovered</returns>
        private IEnumerable<LegacyCSProjPackageReference> GetLegacyCSProjPackageReferences(Array desiredMetadata)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var installedPackages = AsVSProject4.PackageReferences.InstalledPackages;
            var packageReferences = new List<LegacyCSProjPackageReference>();

            foreach (var installedPackage in installedPackages)
            {
                var installedPackageName = installedPackage as string;
                if (!string.IsNullOrEmpty(installedPackageName))
                {
                    string version;
                    Array metadataElements;
                    Array metadataValues;
                    AsVSProject4.PackageReferences.TryGetReference(installedPackageName, desiredMetadata, out version, out metadataElements, out metadataValues);

                    yield return new LegacyCSProjPackageReference(
                        name: installedPackageName,
                        version: version,
                        metadataElements: metadataElements,
                        metadataValues: metadataValues,
                        targetNuGetFramework: _project.TargetNuGetFramework);
                }
            }
        }

        private static ProjectRestoreReference ToProjectRestoreReference(LegacyCSProjProjectReference item)
        {
            var reference = new ProjectRestoreReference()
            {
                ProjectUniqueName = item.UniqueName,
                ProjectPath = item.UniqueName
            };

            MSBuildRestoreUtility.ApplyIncludeFlags(
                reference,
                GetProjectMetadataValue(item, IncludeAssets),
                GetProjectMetadataValue(item, ExcludeAssets),
                GetProjectMetadataValue(item, PrivateAssets));

            return reference;
        }

        private static LibraryDependency ToPackageLibraryDependency(LegacyCSProjPackageReference item)
        {
            var dependency = new LibraryDependency
            {
                LibraryRange = new LibraryRange(
                    name: item.Name,
                    versionRange: VersionRange.Parse(item.Version),
                    typeConstraint: LibraryDependencyTarget.Package)
            };

            MSBuildRestoreUtility.ApplyIncludeFlags(
                dependency,
                GetPackageMetadataValue(item, IncludeAssets),
                GetPackageMetadataValue(item, ExcludeAssets),
                GetPackageMetadataValue(item, PrivateAssets));

            return dependency;
        }

        private static string GetProjectMetadataValue(LegacyCSProjProjectReference item, string metadataElement)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (string.IsNullOrEmpty(metadataElement))
            {
                throw new ArgumentNullException(nameof(metadataElement));
            }

            if (item.MetadataElements == null || item.MetadataValues == null)
            {
                return string.Empty; // no metadata for project
            }

            var index = Array.IndexOf(item.MetadataElements, metadataElement);
            if (index >= 0)
            {
                return item.MetadataValues.GetValue(index) as string;
            }

            return string.Empty;
        }

        private static string GetPackageMetadataValue(LegacyCSProjPackageReference item, string metadataElement)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (string.IsNullOrEmpty(metadataElement))
            {
                throw new ArgumentNullException(nameof(metadataElement));
            }

            if (item.MetadataElements == null || item.MetadataValues == null)
            {
                return string.Empty; // no metadata for package
            }

            var index = Array.IndexOf(item.MetadataElements, metadataElement);
            if (index >= 0)
            {
                return item.MetadataValues.GetValue(index) as string;
            }

            return string.Empty;
        }

        private async Task<T> RunOnUIThread<T>(Func<T> uiThreadFunction)
        {
            if (_callerIsUnitTest)
            {
                return uiThreadFunction();
            }

            var result = default(T);
            await NuGetUIThreadHelper.JoinableTaskFactory.RunAsync(async delegate
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                result = uiThreadFunction();
            });

            return result;
        }

        private void EnsureUIThread()
        {
            if (!_callerIsUnitTest)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
            }
        }
    }
}
