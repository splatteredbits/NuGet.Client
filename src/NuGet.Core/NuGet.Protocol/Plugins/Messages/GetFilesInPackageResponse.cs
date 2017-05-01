// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    public sealed class GetFilesInPackageResponse
    {
        [JsonRequired]
        public MessageResponseCode ResponseCode { get; }

        public IEnumerable<string> Files { get; }

        [JsonConstructor]
        public GetFilesInPackageResponse(MessageResponseCode responseCode, IEnumerable<string> files)
        {
            ResponseCode = responseCode;
            Files = files;
        }
    }
}