using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    public sealed class CopyPackageFilesRequest
    {
        [JsonRequired]
        public string DestinationFolderPath { get; }

        [JsonRequired]
        public IEnumerable<string> FilesInPackage { get; }

        [JsonRequired]
        public string PackageId { get; }

        [JsonRequired]
        public string PackageSourceRepository { get; }

        [JsonRequired]
        public string PackageVersion { get; }

        [JsonConstructor]
        public CopyPackageFilesRequest(
            string packageSourceRepository,
            string packageId,
            string packageVersion,
            IEnumerable<string> filesInPackage,
            string destinationFolderPath)
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
            FilesInPackage = filesInPackage;
            DestinationFolderPath = destinationFolderPath;
        }
    }
}