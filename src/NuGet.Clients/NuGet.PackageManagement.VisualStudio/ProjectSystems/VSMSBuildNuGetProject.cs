// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft;
using NuGet.ProjectManagement;
using NuGet.ProjectModel;
using NuGet.VisualStudio;

namespace NuGet.PackageManagement.VisualStudio
{
    /// <summary>
    /// This is an implementation of <see cref="MSBuildNuGetProject"/> that has knowledge about interacting with DTE.
    /// Since the base class <see cref="MSBuildNuGetProject"/> is in the NuGet.Core solution, it does not have
    /// references to DTE.
    /// </summary>
    public class VSMSBuildNuGetProject : MSBuildNuGetProject
    {
        private readonly IVsProjectAdapter _vsProjectAdapter;

        public VSMSBuildNuGetProject(
            IVsProjectAdapter project,
            IMSBuildNuGetProjectSystem msbuildNuGetProjectSystem,
            string folderNuGetProjectPath,
            string packagesConfigFolderPath) : base(
                msbuildNuGetProjectSystem,
                folderNuGetProjectPath,
                packagesConfigFolderPath)
        {
            _vsProjectAdapter = project;

            // set project id
            var projectId = project.ProjectId;
            InternalMetadata.Add(NuGetProjectMetadataKeys.ProjectId, projectId);
        }

        public override Task<IReadOnlyList<ProjectRestoreReference>> GetDirectProjectReferencesAsync(DependencyGraphCacheContext context)
        {
            Assumes.Present(context);

            var resolvedProjects = context.DeferredPackageSpecs.Select(project => project.Name);
            return VSProjectRestoreReferenceUtility.GetDirectProjectReferencesAsync(_vsProjectAdapter.Project, resolvedProjects, context.Logger);
        }
    }
}
