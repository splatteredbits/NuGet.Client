// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    public sealed class GetCredentialRequest
    {
        [JsonRequired]
        public string PackageSourceRepository { get; }

        [JsonRequired]
        public HttpStatusCode StatusCode { get; }

        [JsonConstructor]
        public GetCredentialRequest(string packageSourceRepository, HttpStatusCode statusCode)
        {
            PackageSourceRepository = packageSourceRepository;
            StatusCode = statusCode;
        }
    }
}