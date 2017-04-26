// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.ProjectManagement;
using NuGet.ProjectModel;
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.VisualStudio
{
    [DebuggerDisplay("{ProjectName}")]
    internal class VsProjectAdapter : IVsProjectAdapter
    {
        private readonly VsHierarchyItem _vsHierarchyItem;
        private EnvDTE.Project _dteProject;
        private readonly Func<IVsHierarchy, EnvDTE.Project> _loadDteProject;
        private readonly IVsProjectAdapterProvider _vsProjectAdapterProvider;
        private readonly IDeferredProjectWorkspaceService _deferredProjectWorkspaceService;

        #region Properties

        public string CustomUniqueName => ProjectNames.CustomUniqueName;

        public string FullName => ProjectNames.FullName;

        public string FullPath
        {
            get
            {
                if (_dteProject != null)
                {
                    return EnvDTEProjectInfoUtility.GetFullPath(_dteProject);
                }
                else
                {
                    return Path.GetDirectoryName(FullProjectPath);
                }
            }
        }

        public string FullProjectPath { get; private set; }

        public bool IsLoadDeferred => _dteProject == null;

        public bool IsSupported
        {
            get
            {
                if (_dteProject != null)
                {
                    return EnvDTEProjectUtility.IsSupported(_dteProject);
                }

                return true;
            }
        }

        public IVsProjectBuildSystem ProjectBuildSystem => EnvDTEProjectUtility.GetVsProjectBuildSystem(DteProject);

        public string ProjectId
        {
            get
            {
                Guid id;
                if (!_vsHierarchyItem.TryGetProjectId(out id))
                {
                    id = Guid.Empty;
                }

                return id.ToString();
            }
        }

        public string ProjectName => ProjectNames.ShortName;

        public ProjectNames ProjectNames { get; private set; }

        public string[] ProjectTypeGuids
        {
            get
            {
                if (_dteProject != null)
                {
                    return VsHierarchyUtility.GetProjectTypeGuids(_dteProject);
                }
                else
                {
                    return VsHierarchyUtility.GetProjectTypeGuids(VsHierarchy);
                }
            }
        }

        public string UniqueName => ProjectNames.UniqueName;

        public EnvDTE.Project DteProject
        {
            get
            {
                if (_dteProject == null)
                {
                    _dteProject = _loadDteProject(_vsHierarchyItem.VsHierarchy);
                }

                return _dteProject;
            }
        }

        public IVsHierarchy VsHierarchy => _vsHierarchyItem.VsHierarchy;

        #endregion Properties

        public VsProjectAdapter(
            EnvDTE.Project dteProject,
            IVsProjectAdapterProvider vsProjectAdapterProvider)
        {
            Assumes.Present(dteProject);
            Assumes.Present(vsProjectAdapterProvider);

            _vsHierarchyItem = VsHierarchyItem.FromDteProject(dteProject);
            _dteProject = dteProject;
            _vsProjectAdapterProvider = vsProjectAdapterProvider;

            FullProjectPath = EnvDTEProjectInfoUtility.GetFullProjectPath(_dteProject);
            ProjectNames = ProjectNames.FromDTEProject(_dteProject);
        }

        public VsProjectAdapter(
            IVsHierarchy project,
            ProjectNames projectNames,
            Func<IVsHierarchy, EnvDTE.Project> loadDteProject,
            IVsProjectAdapterProvider vsProjectAdapterProvider,
            IDeferredProjectWorkspaceService deferredProjectWorkspaceService)
        {
            Assumes.Present(project);
            Assumes.Present(projectNames);
            Assumes.Present(loadDteProject);
            Assumes.Present(vsProjectAdapterProvider);
            Assumes.Present(deferredProjectWorkspaceService);

            _vsHierarchyItem = VsHierarchyItem.FromVsHierarchy(project);
            _loadDteProject = loadDteProject;
            _vsProjectAdapterProvider = vsProjectAdapterProvider;
            _deferredProjectWorkspaceService = deferredProjectWorkspaceService;

            FullProjectPath = VsHierarchyUtility.GetProjectPath(project);
            ProjectNames = projectNames;
        }

        #region Getters

        public HashSet<string> GetAssemblyClosure(IDictionary<string, HashSet<string>> visitedProjects)
        {
            return EnvDTEProjectUtility.GetAssemblyClosure(DteProject, visitedProjects);
        }

        public IEnumerable<string> GetChildItems(string path, string filter, string desiredKind)
        {
            return NuGetUIThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var childItems = await EnvDTEProjectUtility.GetChildItems(DteProject, path, filter, VsProjectTypes.VsProjectItemKindPhysicalFile);
                // Get all physical files
                return from p in childItems
                       select p.Name;
            });
        }

        public string GetConfigurationFile()
        {
            return EnvDTEProjectInfoUtility.GetConfigurationFile(DteProject);
        }

        public async Task<IReadOnlyList<ProjectRestoreReference>> GetDirectProjectReferencesAsync(IEnumerable<string> resolvedProjects, ILogger logger)
        {
            if (_deferredProjectWorkspaceService != null)
            {
                var references = await _deferredProjectWorkspaceService.GetProjectReferencesAsync(FullProjectPath);

                return references
                    .Select(reference => new ProjectRestoreReference
                    {
                        ProjectPath = reference,
                        ProjectUniqueName = reference
                    })
                    .ToList();
            }
            else
            {
                return await VSProjectRestoreReferenceUtility.GetDirectProjectReferencesAsync(DteProject, resolvedProjects, logger);
            }
        }

        public FrameworkName GetDotNetFrameworkName()
        {
            return EnvDTEProjectInfoUtility.GetDotNetFrameworkName(DteProject);
        }

        public IEnumerable<string> GetFullPaths(string fileName)
        {
            return NuGetUIThreadHelper.JoinableTaskFactory.Run(async delegate
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var paths = new List<string>();
                var projectItemsQueue = new Queue<EnvDTE.ProjectItems>();
                projectItemsQueue.Enqueue(DteProject.ProjectItems);
                while (projectItemsQueue.Count > 0)
                {
                    var items = projectItemsQueue.Dequeue();
                    foreach (var item in items.Cast<EnvDTE.ProjectItem>())
                    {
                        if (item.Kind == VsProjectTypes.VsProjectItemKindPhysicalFile)
                        {
                            if (StringComparer.OrdinalIgnoreCase.Equals(item.Name, fileName))
                            {
                                paths.Add(item.FileNames[1]);
                            }
                        }
                        else if (item.Kind == VsProjectTypes.VsProjectItemKindPhysicalFolder)
                        {
                            projectItemsQueue.Enqueue(item.ProjectItems);
                        }
                    }
                }

                return paths;
            });
        }

        public async Task<EnvDTE.ProjectItems> GetProjectItemsAsync(string folderPath, bool createIfNotExists)
        {
            return await EnvDTEProjectUtility.GetProjectItemsAsync(DteProject, folderPath, createIfNotExists);
        }

        public dynamic GetPropertyValue(string propertyName)
        {
            try
            {
                var envDTEProperty = DteProject.Properties.Item(propertyName);
                if (envDTEProperty != null)
                {
                    return envDTEProperty.Value;
                }
            }
            catch (ArgumentException)
            {
                // If the property doesn't exist this will throw an argument exception
            }
            return null;
        }

        public async Task<EnvDTE.ProjectItem> GetProjectItemAsync(string path)
        {
            return await EnvDTEProjectUtility.GetProjectItemAsync(DteProject, path);
        }

        public IList<IVsProjectAdapter> GetReferencedProjects()
        {
            return EnvDTEProjectUtility.GetReferencedProjects(DteProject).Select(project => _vsProjectAdapterProvider.CreateVsProject(project)).ToList();
        }

        public NuGetFramework GetTargetNuGetFramework()
        {
            return EnvDTEProjectInfoUtility.GetTargetNuGetFramework(DteProject);
        }

        public UnconfiguredProject GetUnconfiguredProject()
        {
            var context = DteProject as IVsBrowseObjectContext;
            if (context == null)
            { 
                // VC implements this on their DTE.Project.Object
                context = DteProject.Object as IVsBrowseObjectContext;
            }
            return context?.UnconfiguredProject;
        }

        #endregion Getters

        #region Queries

        public async Task<bool> ContainsFile(string path)
        {
            return await EnvDTEProjectUtility.ContainsFile(DteProject, path);
        }

        public bool SupportsBindingRedirects()
        {
            return EnvDTEProjectUtility.SupportsBindingRedirects(DteProject);
        }

        public bool SupportsReference => EnvDTEProjectUtility.SupportsReferences(DteProject);

        #endregion Queries

        #region Methods

        public void AddImportStatement(string targetsPath, ImportLocation location)
        {
            EnvDTEProjectUtility.AddImportStatement(DteProject, targetsPath, location);
        }

        public async Task<bool> DeleteProjectItemAsync(string path)
        {
            return await EnvDTEProjectUtility.DeleteProjectItemAsync(DteProject, path);
        }

        public void EnsureCheckedOutIfExists(string root, string path)
        {
            EnvDTEProjectUtility.EnsureCheckedOutIfExists(DteProject, root, path);
        }

        public void RemoveImportStatement(string targetsPath)
        {
            EnvDTEProjectUtility.RemoveImportStatement(DteProject, targetsPath);
        }

        public void Save()
        {
            EnvDTEProjectUtility.Save(DteProject);
        }
        
        #endregion Methods
    }
}
