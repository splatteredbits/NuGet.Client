// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Moq;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using Xunit;

namespace NuGet.Protocol.Plugins.Tests
{
    public class PluginFindPackageByIdResourceTests
    {
        private readonly PluginCredentialProvider _credentialProvider;
        private readonly Mock<ICredentialService> _credentialService;
        private readonly Mock<HttpHandlerResource> _httpHandlerResource;
        private readonly PackageSource _packageSource;
        private readonly PluginResource _pluginResource;

        public PluginFindPackageByIdResourceTests()
        {
            _pluginResource = new PluginResource(Enumerable.Empty<PluginCreationResult>());
            _packageSource = new PackageSource(source: "");
            _httpHandlerResource = new Mock<HttpHandlerResource>();
            _credentialService = new Mock<ICredentialService>();
            _credentialProvider = new PluginCredentialProvider(
                _pluginResource,
                _packageSource,
                _httpHandlerResource.Object,
                _credentialService.Object);

            HttpHandlerResourceV3.CredentialService = Mock.Of<ICredentialService>();
        }

        [Fact]
        public void Constructor_ThrowsForNullPluginResource()
        {
            var exception = Assert.Throws<ArgumentNullException>(
                () => new PluginFindPackageByIdResource(
                    pluginResource: null,
                    packageSource: _packageSource,
                    credentialProvider: _credentialProvider));

            Assert.Equal("pluginResource", exception.ParamName);
        }

        [Fact]
        public void Constructor_ThrowsForNullPackageSource()
        {
            var exception = Assert.Throws<ArgumentNullException>(
                () => new PluginFindPackageByIdResource(
                    _pluginResource,
                    packageSource: null,
                    credentialProvider: _credentialProvider));

            Assert.Equal("packageSource", exception.ParamName);
        }

        [Fact]
        public void Constructor_ThrowsForNullCredentialProvider()
        {
            var exception = Assert.Throws<ArgumentNullException>(
                () => new PluginFindPackageByIdResource(
                    _pluginResource,
                    _packageSource,
                    credentialProvider: null));

            Assert.Equal("credentialProvider", exception.ParamName);
        }
    }
}