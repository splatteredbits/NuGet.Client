using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    public sealed class GetPackageVersionsRequest
    {
        [JsonRequired]
        public string PackageId { get; }

        [JsonRequired]
        public string PackageSourceRepository { get; }

        [JsonConstructor]
        public GetPackageVersionsRequest(
            string packageSourceRepository,
            string packageId)
        {
            PackageSourceRepository = packageSourceRepository;
            PackageId = packageId;
        }
    }
}