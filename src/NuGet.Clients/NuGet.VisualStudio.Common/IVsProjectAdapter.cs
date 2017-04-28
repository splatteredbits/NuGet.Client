// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.ProjectManagement;
using NuGet.ProjectModel;
using NuGet.RuntimeModel;
using VSLangProj;

namespace NuGet.VisualStudio
{
    public interface IVsProjectAdapter
    {
        /// <summary>
        /// BaseIntermediateOutputPath project property (e.g. c:\projFoo\obj)
        /// </summary>
        string BaseIntermediateOutputPath { get; }

        string CustomUniqueName { get; }
        string FullName { get; }
        string FullPath { get; }
        string FullProjectPath { get; }
        bool IsLoadDeferred { get; }
        bool IsSupported { get; }

        /// <summary>
        /// PackageTargetFallback project property
        /// </summary>
        string PackageTargetFallback { get; }

        IVsProjectBuildSystem ProjectBuildSystem { get; }
        string ProjectId { get; }
        string ProjectName { get; }
        ProjectNames ProjectNames { get; }
        string[] ProjectTypeGuids { get; }

        /// <summary>
        /// Project's runtime identifiers. Should never be null but can be an empty sequence.
        /// </summary>
        IEnumerable<RuntimeDescription> Runtimes { get; }

        /// <summary>
        /// Project's supports (a.k.a guardrails). Should never be null but can be an empty sequence.
        /// </summary>
        IEnumerable<CompatibilityProfile> Supports { get; }

        /// <summary>
        /// Project's target framework
        /// </summary>
        NuGetFramework TargetNuGetFramework { get; }

        string UniqueName { get; }

        /// <summary>
        /// Version
        /// </summary>
        string Version { get; }

        /// <summary>
        /// In unavoidable circumstances where we need to DTE object, it's exposed here
        /// </summary>
        EnvDTE.Project Project { get; }
        IVsHierarchy VsHierarchy { get; }

        HashSet<string> GetAssemblyClosure(IDictionary<string, HashSet<string>> visitedProjects);
        IEnumerable<string> GetChildItems(string path, string filter, string desiredKind);
        string GetConfigurationFile();
        Task<IReadOnlyList<ProjectRestoreReference>> GetDirectProjectReferencesAsync(IEnumerable<string> resolvedProjects, ILogger log);
        FrameworkName GetDotNetFrameworkName();
        IEnumerable<string> GetFullPaths(string fileName);
        string GetBuildProperty(string propertyName);
        Task<EnvDTE.ProjectItem> GetProjectItemAsync(string path);
        Task<EnvDTE.ProjectItems> GetProjectItemsAsync(string folderPath, bool createIfNotExists);
        dynamic GetProjectProperty(string propertyName);
        IList<IVsProjectAdapter> GetReferencedProjects();
        References GetReferences();
        NuGetFramework GetTargetNuGetFramework();
        UnconfiguredProject GetUnconfiguredProject();

        Task<bool> ContainsFile(string path);
        bool SupportsBindingRedirects { get; }
        bool SupportsProjectSystemService { get; }
        bool SupportsReference { get; }

        void AddImportStatement(string targetsPath, ImportLocation location);
        Task<bool> DeleteProjectItemAsync(string path);
        void EnsureCheckedOutIfExists(string root, string path);
        void RemoveImportStatement(string targetsPath);
        void Save();
    }
}
