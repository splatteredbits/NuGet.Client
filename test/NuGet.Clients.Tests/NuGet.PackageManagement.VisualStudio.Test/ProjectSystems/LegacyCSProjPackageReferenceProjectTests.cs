// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Moq;
using NuGet.Frameworks;
using NuGet.ProjectManagement;
using NuGet.RuntimeModel;
using NuGet.Test.Utility;
using NuGet.Versioning;
using NuGet.VisualStudio;
using Test.Utility.Threading;
using VSLangProj150;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace NuGet.PackageManagement.VisualStudio.Test
{
    [Collection(DispatcherThreadCollection.CollectionName)]
    public class LegacyCSProjPackageReferenceProjectTests
    {
        private readonly JoinableTaskFactory _jtf;

        public LegacyCSProjPackageReferenceProjectTests(DispatcherThreadFixture fixture)
        {
            Assumes.Present(fixture);

            _jtf = fixture.JoinableTaskFactory;
        }

        [Fact]
        public async Task GetAssetsFilePathAsync_WithValidBaseIntermediateOutputPath_Succeeds()
        {
            // Arrange
            using (var testDirectory = TestDirectory.Create())
            {
                var testBaseIntermediateOutputPath = Path.Combine(testDirectory, "obj");
                TestDirectory.Create(testBaseIntermediateOutputPath);
                var projectAdapter = Mock.Of<IVsProjectAdapter>();
                Mock.Get(projectAdapter)
                    .SetupGet(x => x.BaseIntermediateOutputPath)
                    .Returns(testBaseIntermediateOutputPath);

                var testProject = new LegacyCSProjPackageReferenceProject(
                    project: projectAdapter,
                    projectId: Guid.NewGuid().ToString())
                {
                    JoinableTaskFactory = _jtf
                };

                await _jtf.SwitchToMainThreadAsync();

                // Act
                var assetsPath = await testProject.GetAssetsFilePathAsync();

                // Assert
                Assert.Equal(Path.Combine(testBaseIntermediateOutputPath, "project.assets.json"), assetsPath);
            }
        }

        [Fact]
        public async Task GetAssetsFilePathAsync_WithNoBaseIntermediateOutputPath_Throws()
        {
            // Arrange
            using (TestDirectory.Create())
            {
                var testProject = new LegacyCSProjPackageReferenceProject(
                    project: Mock.Of<IVsProjectAdapter>(),
                    projectId: Guid.NewGuid().ToString())
                {
                    JoinableTaskFactory = _jtf
                };

                await _jtf.SwitchToMainThreadAsync();

                // Act & Assert
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => testProject.GetAssetsFilePathAsync());
            }
        }

        [Fact]
        public async Task GetPackageSpecsAsync_WithPackageTargetFallback_Succeeds()
        {
            // Arrange
            using (var testDirectory = TestDirectory.Create())
            {
                var projectAdapter = CreateProjectAdapter(testDirectory);
                Mock.Get(projectAdapter)
                    .SetupGet(x => x.PackageTargetFallback)
                    .Returns("portable-net45+win8;dnxcore50");

                var testProject = new LegacyCSProjPackageReferenceProject(
                    project: projectAdapter,
                    projectId: Guid.NewGuid().ToString())
                {
                    JoinableTaskFactory = _jtf
                };

                var testDependencyGraphCacheContext = new DependencyGraphCacheContext();

                await _jtf.SwitchToMainThreadAsync();

                // Act
                var packageSpecs = await testProject.GetPackageSpecsAsync(testDependencyGraphCacheContext);

                // Assert
                var actualTfi = packageSpecs.First().TargetFrameworks.First();
                Assert.NotNull(actualTfi);
                Assert.Equal(
                    new List<NuGetFramework>()
                    {
                        NuGetFramework.Parse("portable-net45+win8"), NuGetFramework.Parse("dnxcore50")
                    },
                    actualTfi.Imports.ToList());
                Assert.IsType<FallbackFramework>(actualTfi.FrameworkName);
                Assert.Equal(
                    new List<NuGetFramework>()
                    {
                        NuGetFramework.Parse("portable-net45+win8"), NuGetFramework.Parse("dnxcore50")
                    },
                    ((FallbackFramework)actualTfi.FrameworkName).Fallback);
            }
        }

        [Fact]
        public async Task GetPackageSpecsAsync_WithVersion_Succeeds()
        {
            // Arrange
            using (var testDirectory = TestDirectory.Create())
            {
                var projectAdapter = CreateProjectAdapter(testDirectory);
                Mock.Get(projectAdapter)
                    .SetupGet(x => x.Version)
                    .Returns("2.2.3");

                var testProject = new LegacyCSProjPackageReferenceProject(
                    project: projectAdapter,
                    projectId: Guid.NewGuid().ToString())
                {
                    JoinableTaskFactory = _jtf
                };

                var testDependencyGraphCacheContext = new DependencyGraphCacheContext();

                await _jtf.SwitchToMainThreadAsync();

                // Act
                var packageSpecs = await testProject.GetPackageSpecsAsync(testDependencyGraphCacheContext);

                // Assert
                Assert.Equal("2.2.3", packageSpecs.First().Version.ToString());
            }
        }

        [Fact]
        public async Task GetPackageSpecsAsync_WithDefaultVersion_Succeeds()
        {
            // Arrange
            using (var testDirectory = TestDirectory.Create())
            {
                var projectAdapter = CreateProjectAdapter(testDirectory);

                var testProject = new LegacyCSProjPackageReferenceProject(
                    project: projectAdapter,
                    projectId: Guid.NewGuid().ToString())
                {
                    JoinableTaskFactory = _jtf
                };

                var testDependencyGraphCacheContext = new DependencyGraphCacheContext();

                await _jtf.SwitchToMainThreadAsync();

                // Act
                var packageSpecs = await testProject.GetPackageSpecsAsync(testDependencyGraphCacheContext);

                // Assert
                Assert.Equal("1.0.0", packageSpecs.First().Version.ToString());
            }
        }

        [Fact]
        public async Task GetPackageSpecsAsync_WithPackageReference_Succeeds()
        {
            // Arrange
            using (var randomTestFolder = TestDirectory.Create())
            {
                var projectAdapter = CreateProjectAdapter(randomTestFolder);

                var installedPackages = new[] { "packageA" };
                var vsProject4 = Mock.Get(projectAdapter.Project.Object as VSProject4);
                var packageReferences = Mock.Get(vsProject4.Object.PackageReferences);
                packageReferences
                    .SetupGet(x => x.InstalledPackages)
                    .Returns(installedPackages);
                var version = "1.*";
                Array metadataElements = null;
                Array metadataValues = null;
                packageReferences
                    .Setup(x => x.TryGetReference("packageA", It.IsAny<Array>(), out version, out metadataElements, out metadataValues))
                    .Returns(true);

                var testProject = new LegacyCSProjPackageReferenceProject(
                    project: projectAdapter,
                    projectId: Guid.NewGuid().ToString())
                {
                    JoinableTaskFactory = _jtf
                };

                var testDependencyGraphCacheContext = new DependencyGraphCacheContext();

                await _jtf.SwitchToMainThreadAsync();

                // Act
                var packageSpecs = await testProject.GetPackageSpecsAsync(testDependencyGraphCacheContext);

                // Assert
                var dependency = packageSpecs.First().Dependencies.First();
                Assert.NotNull(dependency);
                Assert.Equal("packageA", dependency.LibraryRange.Name);
                Assert.Equal(VersionRange.Parse("1.*"), dependency.LibraryRange.VersionRange);
            }
        }

        private static Mock<IVsProjectAdapter> CreateProjectAdapter()
        {
            var projectAdapter = new Mock<IVsProjectAdapter>();

            var project = Mock.Of<EnvDTE.Project>();

            var vsProject4 = Mock.Of<VSProject4>();
            Mock.Get(project)
                .SetupGet(x => x.Object)
                .Returns(vsProject4);

            var packageReferences = Mock.Of<PackageReferences>();
            Mock.Get(vsProject4)
                .SetupGet(x => x.PackageReferences)
                .Returns(packageReferences);

            var installedPackages = new string[] { };
            Mock.Get(packageReferences)
                .SetupGet(x => x.InstalledPackages)
                .Returns(installedPackages);

            projectAdapter
                .SetupGet(x => x.Project)
                .Returns(project);
            projectAdapter
                .Setup(x => x.GetRuntimes())
                .Returns(Enumerable.Empty<RuntimeDescription>);
            projectAdapter
                .Setup(x => x.Supports)
                .Returns(Enumerable.Empty<CompatibilityProfile>);
            projectAdapter
                .Setup(x => x.Version)
                .Returns("1.0.0");
            return projectAdapter;
        }

        private static IVsProjectAdapter CreateProjectAdapter(string fullPath)
        {
            var projectAdapter = CreateProjectAdapter();
            projectAdapter
                .Setup(x => x.FullPath)
                .Returns(Path.Combine(fullPath, "foo.csproj"));
            projectAdapter
                .Setup(x => x.GetTargetFramework())
                .Returns(new NuGetFramework("netstandard13"));

            var testBaseIntermediateOutputPath = Path.Combine(fullPath, "obj");
            TestDirectory.Create(testBaseIntermediateOutputPath);
            projectAdapter
                .Setup(x => x.BaseIntermediateOutputPath)
                .Returns(testBaseIntermediateOutputPath);

            return projectAdapter.Object;
        }
    }
}
