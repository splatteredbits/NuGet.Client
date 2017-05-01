using System.Collections.Generic;
using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    public class GetPackageVersionsResponse
    {
        [JsonRequired]
        public MessageResponseCode ResponseCode { get; }

        public IEnumerable<string> Versions { get; }

        [JsonConstructor]
        public GetPackageVersionsResponse(MessageResponseCode responseCode, IEnumerable<string> versions)
        {
            ResponseCode = responseCode;
            Versions = versions;
        }
    }
}