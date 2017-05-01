// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    public sealed class GetFilesInPackageRequest
    {
        [JsonRequired]
        public string PackageId { get; }

        [JsonRequired]
        public string PackageSourceRepository { get; }

        [JsonRequired]
        public string PackageVersion { get; }

        [JsonConstructor]
        public GetFilesInPackageRequest(
            string packageSourceRepository,
            string packageId,
            string packageVersion)
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

            PackageId = packageId;
            PackageVersion = packageVersion;
            PackageSourceRepository = packageSourceRepository;
        }
    }
}