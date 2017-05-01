using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    public class CopyPackageFilesResponse
    {
        [JsonRequired]
        public IEnumerable<string> CopiedFiles { get; }

        [JsonRequired]
        public MessageResponseCode ResponseCode { get; }

        [JsonConstructor]
        public CopyPackageFilesResponse(MessageResponseCode responseCode, IEnumerable<string> copiedFiles)
        {
            ResponseCode = responseCode;
            CopiedFiles = copiedFiles;
        }
    }
}