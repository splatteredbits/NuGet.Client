// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    public sealed class GetFileInPackageRequest
    {
        [JsonRequired]
        public string DownloadFilePath { get; }

        [JsonRequired]
        public string PackageId { get; }

        [JsonRequired]
        public string PackageSourceRepository { get; }

        [JsonRequired]
        public string PackageVersion { get; }

        [JsonRequired]
        public string PathInPackage { get; }

        [JsonConstructor]
        public GetFileInPackageRequest(
            string packageSourceRepository,
            string packageId,
            string packageVersion,
            string pathInPackage,
            string downloadFilePath)
        {
            if (string.IsNullOrEmpty(packageSourceRepository))
            {
                throw new ArgumentException(Strings.ArgumentCannotBeNullOrEmpty, nameof(packageSourceRepository));
            }

            if (string.IsNullOrEmpty(packageId))
            {
                throw new ArgumentException(Strings.ArgumentCannotBeNullOrEmpty, nameof(packageId));
            }

            if (string.IsNullOrEmpty(packageVersion))
            {
                throw new ArgumentException(Strings.ArgumentCannotBeNullOrEmpty, nameof(packageVersion));
            }

            if (string.IsNullOrEmpty(pathInPackage))
            {
                throw new ArgumentException(Strings.ArgumentCannotBeNullOrEmpty, nameof(pathInPackage));
            }

            if (string.IsNullOrEmpty(downloadFilePath))
            {
                throw new ArgumentException(Strings.ArgumentCannotBeNullOrEmpty, nameof(downloadFilePath));
            }

            PackageId = packageId;
            PackageVersion = packageVersion;
            PathInPackage = pathInPackage;
            PackageSourceRepository = packageSourceRepository;
            DownloadFilePath = downloadFilePath;
        }
    }
}