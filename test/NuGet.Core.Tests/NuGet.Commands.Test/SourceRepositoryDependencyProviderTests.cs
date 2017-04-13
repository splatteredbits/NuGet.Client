// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Test.Utility;
using NuGet.Versioning;
using Xunit;

namespace NuGet.Commands.Test
{
    public class SourceRepositoryDependencyProviderTests
    {
        [Fact]
        public async Task SourceRepositoryDependencyProvider_VerifyFindLibraryAsyncThrowsWhenListedPackageIsMissing()
        {
            // Arrange
            var testLogger = new TestLogger();
            var cacheContext = new SourceCacheContext();

            var findResource = new Mock<FindPackageByIdResource>();
            findResource.Setup(s => s.GetAllVersionsAsync(It.IsAny<string>(), It.IsAny<SourceCacheContext>(), It.IsAny<ILogger>(), It.IsAny<CancellationToken>()))
                            .ReturnsAsync(new[] { NuGetVersion.Parse("1.0.0"), NuGetVersion.Parse("2.0.0") });

            findResource.Setup(s => s.GetOriginalIdentityAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<SourceCacheContext>(), It.IsAny<ILogger>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new PackageNotFoundProtocolException(new PackageIdentity("x", NuGetVersion.Parse("1.0.0"))));

            var source = new Mock<SourceRepository>();
            source.Setup(s => s.GetResourceAsync<FindPackageByIdResource>())
                .ReturnsAsync(findResource.Object);
            source.SetupGet(s => s.PackageSource)
                .Returns(new PackageSource("http://test/index.json"));

            var libraryRange = new LibraryRange("x", new VersionRange(new NuGetVersion(1, 0, 0)), LibraryDependencyTarget.Package);
            var provider = new SourceRepositoryDependencyProvider(source.Object, testLogger, cacheContext, ignoreFailedSources: true, ignoreWarning: true);

            // Act && Assert
            await Assert.ThrowsAsync<FatalProtocolException>(async () =>
                await provider.FindLibraryAsync(libraryRange, NuGetFramework.Parse("net45"), cacheContext, testLogger, CancellationToken.None));
        }

        [Fact]
        public async Task SourceRepositoryDependencyProvider_VerifyFindLibraryAsyncReturnsOriginalIdentity()
        {
            // Arrange
            var testLogger = new TestLogger();
            var cacheContext = new SourceCacheContext();

            var findResource = new Mock<FindPackageByIdResource>();
            findResource.Setup(s => s.GetAllVersionsAsync(It.IsAny<string>(), It.IsAny<SourceCacheContext>(), It.IsAny<ILogger>(), It.IsAny<CancellationToken>()))
                            .ReturnsAsync(new[] { NuGetVersion.Parse("1.0.0-beta"), NuGetVersion.Parse("2.0.0") });

            findResource.Setup(s => s.GetOriginalIdentityAsync(It.IsAny<string>(), It.IsAny<NuGetVersion>(), It.IsAny<SourceCacheContext>(), It.IsAny<ILogger>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PackageIdentity("X", NuGetVersion.Parse("1.0.0-bEta")));

            var source = new Mock<SourceRepository>();
            source.Setup(s => s.GetResourceAsync<FindPackageByIdResource>())
                .ReturnsAsync(findResource.Object);
            source.SetupGet(s => s.PackageSource)
                .Returns(new PackageSource("http://test/index.json"));

            var libraryRange = new LibraryRange("x", new VersionRange(new NuGetVersion(1, 0, 0)), LibraryDependencyTarget.Package);
            var provider = new SourceRepositoryDependencyProvider(source.Object, testLogger, cacheContext, ignoreFailedSources: true, ignoreWarning: true);

            // Act
            var library = await provider.FindLibraryAsync(libraryRange, NuGetFramework.Parse("net45"), cacheContext, testLogger, CancellationToken.None);

            // Assert
            Assert.Equal("X", library.Name);
            Assert.Equal("1.0.0-bEta", library.Version.ToString());
        }
    }
}
