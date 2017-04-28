// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities;
using NuGet.ProjectManagement;
using NuGet.ProjectModel;
using NuGet.VisualStudio;
using VSLangProj150;
using ProjectSystem = Microsoft.VisualStudio.ProjectSystem;

namespace NuGet.PackageManagement.VisualStudio
{
    [Export(typeof(IProjectSystemProvider))]
    [Name(nameof(LegacyCSProjPackageReferenceProjectProvider))]
    [Order(After = nameof(CpsPackageReferenceProjectProvider))]
    public class LegacyCSProjPackageReferenceProjectProvider : IProjectSystemProvider
    {
        private const string RestoreProjectStyle = nameof(RestoreProjectStyle);

        private readonly IProjectSystemCache _projectSystemCache;

        // Reason it's lazy<object> is because we don't want to load any CPS assemblies untill
        // we're really going to use any of CPS api. Which is why we also don't use nameof or typeof apis.
        [Import("Microsoft.VisualStudio.ProjectSystem.IProjectServiceAccessor")]
        private Lazy<object> ProjectServiceAccessor { get; set; }

        [ImportingConstructor]
        public LegacyCSProjPackageReferenceProjectProvider(IProjectSystemCache projectSystemCache)
        {
            Assumes.Present(projectSystemCache);

            _projectSystemCache = projectSystemCache;
        }
        
        public bool TryCreateNuGetProject(IVsProjectAdapter vsProjectAdapter, ProjectSystemProviderContext context, out NuGetProject result)
        {
            Assumes.Present(vsProjectAdapter);
            Assumes.Present(context);

            ThreadHelper.ThrowIfNotOnUIThread();

            result = null;

            if (vsProjectAdapter.IsLoadDeferred || !IsLegacyCSProjPackageReferenceProject(vsProjectAdapter))
            {
                return false;
            }

            // Lazy load the CPS enabled JoinableTaskFactory for the UI.
            NuGetUIThreadHelper.SetJoinableTaskFactoryFromService(ProjectServiceAccessor.Value as ProjectSystem.IProjectServiceAccessor);

            result = new LegacyCSProjPackageReferenceProject(
                vsProjectAdapter,
                vsProjectAdapter.ProjectId);

            return true;
        }

        /// <summary>
        /// Is this project a non-CPS package reference based csproj?
        /// </summary>
        public bool IsLegacyCSProjPackageReferenceProject(IVsProjectAdapter _project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var isLegacyCSProjPackageReferenceProject = false;
            var asVSProject4 = _project.Project.Object as VSProject4;

            // A legacy CSProj must cast to VSProject4 to manipulate package references
            if (asVSProject4 == null)
            {
                isLegacyCSProjPackageReferenceProject = false;
            }
            else
            {
                // Check for RestoreProjectStyle property
                var restoreProjectStyle = _project.GetBuildProperty(RestoreProjectStyle);

                // For legacy csproj, either the RestoreProjectStyle must be set to PackageReference or
                // project has atleast one package dependency defined as PackageReference
                if (restoreProjectStyle?.Equals(ProjectStyle.PackageReference.ToString(), StringComparison.OrdinalIgnoreCase) ?? false
                    || (asVSProject4.PackageReferences?.InstalledPackages?.Length ?? 0) > 0)
                {
                    isLegacyCSProjPackageReferenceProject = true;
                }
                else
                {
                    isLegacyCSProjPackageReferenceProject = false;
                }
            }

            return isLegacyCSProjPackageReferenceProject;
        }
    }
}
