// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;

namespace NuGet.Protocol.Plugins
{
    public sealed class PluginCredentialProvider : IRequestHandler
    {
        private const string BasicAuthenticationType = "basic";

        private readonly PluginResource _pluginResource;
        private readonly PackageSource _packageSource;
        private readonly HttpHandlerResource _httpHandler;
        private readonly ICredentialService _credentialService;

        public PluginCredentialProvider(
            PluginResource pluginResource,
            PackageSource packageSource,
            HttpHandlerResource httpHandler,
            ICredentialService credentialService)
        {
            if (pluginResource == null)
            {
                throw new ArgumentNullException(nameof(pluginResource));
            }

            if (packageSource == null)
            {
                throw new ArgumentNullException(nameof(packageSource));
            }

            if (httpHandler == null)
            {
                throw new ArgumentNullException(nameof(httpHandler));
            }

            if (credentialService == null)
            {
                throw new ArgumentNullException(nameof(credentialService));
            }

            _pluginResource = pluginResource;
            _packageSource = packageSource;
            _httpHandler = httpHandler;
            _credentialService = credentialService;
        }

        public CancellationToken CancellationToken => CancellationToken.None;

        public Task HandleCancelAsync(IConnection connection, Message request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task HandleProgressAsync(IConnection connection, Message request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task HandleResponseAsync(
            IConnection connection,
            Message request,
            IResponseHandler responseHandler,
            CancellationToken cancellationToken)
        {
            var requestPayload = MessageUtilities.DeserializePayload<GetCredentialRequest>(request);
            GetCredentialResponse responsePayload;

            if (_packageSource.IsHttp &&
                string.Equals(requestPayload.PackageSourceRepository, _packageSource.Source, StringComparison.OrdinalIgnoreCase))
            {
                var credential = await GetCredentialAsync(requestPayload.StatusCode, cancellationToken);

                if (credential == null)
                {
                    responsePayload = new GetCredentialResponse(MessageResponseCode.Error);
                }
                else
                {
                    responsePayload = new GetCredentialResponse(
                        MessageResponseCode.Success,
                        credential.UserName,
                        credential.Password);
                }
            }
            else
            {
                responsePayload = new GetCredentialResponse(MessageResponseCode.Error);
            }

            await responseHandler.SendResponseAsync(request, responsePayload, cancellationToken);
        }

        public async Task<NetworkCredential> GetCredentialAsync(
            HttpStatusCode statusCode,
            CancellationToken cancellationToken)
        {
            var requestType = GetCredentialRequestType(statusCode);

            if (requestType == CredentialRequestType.Proxy)
            {
                return await GetProxyCredentialAsync(cancellationToken);
            }

            return await GetPackageSourceCredential(requestType, cancellationToken);
        }

        private async Task<NetworkCredential> GetPackageSourceCredential(CredentialRequestType requestType, CancellationToken cancellationToken)
        {
            if (_packageSource.Credentials != null && _packageSource.Credentials.IsValid())
            {
                return new NetworkCredential(_packageSource.Credentials.Username, _packageSource.Credentials.Password);
            }

            if (_httpHandler != null)
            {
                string message;
                if (requestType == CredentialRequestType.Unauthorized)
                {
                    message = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Http_CredentialsForUnauthorized,
                        _packageSource.Source);
                }
                else
                {
                    message = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Http_CredentialsForForbidden,
                        _packageSource.Source);
                }

                var credentialService = HttpHandlerResourceV3.CredentialService;
                var sourceUri = _packageSource.SourceUri;
                var proxy = _httpHandler.ClientHandler.Proxy;
                var credentials = await credentialService.GetCredentialsAsync(
                    sourceUri,
                    proxy,
                    requestType,
                    message,
                    cancellationToken);

                return credentials.GetCredential(sourceUri, authType: null);
            }

            return null;
        }

        private async Task<NetworkCredential> GetProxyCredentialAsync(CancellationToken cancellationToken)
        {
            if (_httpHandler != null)
            {
                var proxy = _httpHandler.ClientHandler.Proxy;

                if (proxy != null)
                {
                    var sourceUri = _packageSource.SourceUri;
                    var credentialService = HttpHandlerResourceV3.CredentialService;
                    var proxyUri = proxy.GetProxy(sourceUri);
                    var message = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Http_CredentialsForProxy,
                        proxyUri);
                    var proxyCredentials = await credentialService.GetCredentialsAsync(
                        sourceUri,
                        proxy,
                        CredentialRequestType.Proxy,
                        message,
                        cancellationToken);

                    return proxyCredentials?.GetCredential(proxyUri, BasicAuthenticationType);
                }
            }

            return null;
        }

        private static CredentialRequestType GetCredentialRequestType(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case HttpStatusCode.ProxyAuthenticationRequired:
                    return CredentialRequestType.Proxy;

                case HttpStatusCode.Unauthorized:
                    return CredentialRequestType.Unauthorized;

                case HttpStatusCode.Forbidden:
                default:
                    return CredentialRequestType.Forbidden;
            }
        }
    }
}