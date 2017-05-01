// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    public sealed class GetCredentialResponse
    {
        public string PackageSourcePassword { get; }

        public string PackageSourceUserName { get; }

        [JsonRequired]
        public MessageResponseCode ResponseCode { get; }

        public GetCredentialResponse(MessageResponseCode responseCode)
        {
            ResponseCode = responseCode;
        }

        [JsonConstructor]
        public GetCredentialResponse(
            MessageResponseCode responseCode,
            string packageSourceUserName,
            string packageSourcePassword)
        {
            ResponseCode = responseCode;
            PackageSourceUserName = packageSourceUserName;
            PackageSourcePassword = packageSourcePassword;
        }
    }
}