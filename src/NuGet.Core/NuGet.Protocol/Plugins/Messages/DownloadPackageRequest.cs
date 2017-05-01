// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;
using NuGet.Packaging;

namespace NuGet.Protocol.Plugins
{
    public sealed class DownloadPackageRequest
    {
        [JsonRequired]
        public string HashAlgorithm { get; }

        [JsonRequired]
        public string HashFilePath { get; }

        [JsonRequired]
        public string InstallationFolderPath { get; }

        [JsonRequired]
        public string PackageId { get; }

        [JsonRequired]
        public string PackageVersion { get; }

        [JsonRequired]
        public PackageSaveMode PackageSaveMode { get; }

        [JsonRequired]
        public string PackageSourceRepository { get; }

        [JsonRequired]
        public XmlDocFileSaveMode XmlDocFileSaveMode { get; }

        [JsonConstructor]
        public DownloadPackageRequest(
            string packageSourceRepository,
            string packageId,
            string packageVersion,
            string installationFolderPath,
            PackageSaveMode packageSaveMode,
            XmlDocFileSaveMode xmlDocFileSaveMode,
            string hashFilePath,
            string hashAlgorithm)
        {
            PackageSourceRepository = packageSourceRepository;
            PackageId = packageId;
            PackageVersion = packageVersion;
            InstallationFolderPath = installationFolderPath;
            PackageSaveMode = packageSaveMode;
            XmlDocFileSaveMode = xmlDocFileSaveMode;
            HashFilePath = hashFilePath;
            HashAlgorithm = hashAlgorithm;
        }
    }
}