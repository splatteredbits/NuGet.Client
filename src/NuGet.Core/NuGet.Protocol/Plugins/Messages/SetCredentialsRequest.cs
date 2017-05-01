// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace NuGet.Protocol.Plugins
{
    public sealed class SetCredentialsRequest
    {
        [JsonRequired]
        public string PackageSourceRepository { get; }

        public string ProxyUsername { get; }

        public string ProxyPassword { get; }

        public string Username { get; }

        public string Password { get; }

        public SetCredentialsRequest(
            string packageSourceRepository,
            string proxyUsername,
            string proxyPassword,
            string username,
            string password)
        {
            PackageSourceRepository = packageSourceRepository;
            ProxyUsername = proxyUsername;
            ProxyPassword = proxyPassword;
            Username = username;
            Password = password;
        }
    }
}