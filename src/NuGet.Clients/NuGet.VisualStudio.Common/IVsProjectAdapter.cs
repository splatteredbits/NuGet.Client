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

namespace NuGet.VisualStudio
{
    public interface IVsProjectAdapter
    {
        string CustomUniqueName { get; }
        string FullName { get; }
        string FullPath { get; }
        string FullProjectPath { get; }
        bool IsLoadDeferred { get; }
        bool IsSupported { get; }
        IVsProjectBuildSystem ProjectBuildSystem { get; }
        string ProjectId { get; }
        string ProjectName { get; }
        ProjectNames ProjectNames { get; }
        string[] ProjectTypeGuids { get; }
        string UniqueName { get; }

        EnvDTE.Project DteProject { get; }
        IVsHierarchy VsHierarchy { get; }

        HashSet<string> GetAssemblyClosure(IDictionary<string, HashSet<string>> visitedProjects);
        IEnumerable<string> GetChildItems(string path, string filter, string desiredKind);
        string GetConfigurationFile();
        Task<IReadOnlyList<ProjectRestoreReference>> GetDirectProjectReferencesAsync(IEnumerable<string> resolvedProjects, ILogger log);
        FrameworkName GetDotNetFrameworkName();
        IEnumerable<string> GetFullPaths(string fileName);
        Task<EnvDTE.ProjectItem> GetProjectItemAsync(string path);
        Task<EnvDTE.ProjectItems> GetProjectItemsAsync(string folderPath, bool createIfNotExists);
        dynamic GetPropertyValue(string propertyName);
        IList<IVsProjectAdapter> GetReferencedProjects();
        NuGetFramework GetTargetNuGetFramework();
        UnconfiguredProject GetUnconfiguredProject();

        Task<bool> ContainsFile(string path);
        bool SupportsBindingRedirects();
        bool SupportsReference { get; }

        void AddImportStatement(string targetsPath, ImportLocation location);
        Task<bool> DeleteProjectItemAsync(string path);
        void EnsureCheckedOutIfExists(string root, string path);
        void RemoveImportStatement(string targetsPath);
        void Save();
    }
}
