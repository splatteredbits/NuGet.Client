// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol.Plugins;

namespace NuGet.Protocol.Core.Types
{
    /// <summary>
    /// Represents a plugin resource.
    /// </summary>
    public sealed class PluginResource : INuGetResource
    {
        private readonly PackageSource _packageSource;
        private readonly IReadOnlyList<PluginCreationResult> _pluginCreationResults;
        private bool _hasLoggedWarnings;

        /// <summary>
        /// Instantiates a new <see cref="PluginResource" /> class.
        /// </summary>
        /// <param name="pluginCreationResults">Plugin creation results.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="pluginCreationResults" />
        /// is <c>null</c>.</exception>
        public PluginResource(IEnumerable<PluginCreationResult> pluginCreationResults, PackageSource packageSource = null)
        {
            if (pluginCreationResults == null)
            {
                throw new ArgumentNullException(nameof(pluginCreationResults));
            }

            _pluginCreationResults = pluginCreationResults.ToArray();
            _packageSource = packageSource;
        }

        /// <summary>
        /// Gets the first plugin satisfying the required operation claims for the current package source.
        /// </summary>
        /// <param name="requiredClaim">The required operation claim.</param>
        /// <param name="logger">A logger.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.
        /// The task result (<see cref="Task{TResult}.Result" />) returns a <see cref="IPlugin" />.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="logger" /> is <c>null</c>.</exception>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken" />
        /// is cancelled.</exception>
        public async Task<IPlugin> GetPluginAsync(
            OperationClaim requiredClaim,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            cancellationToken.ThrowIfCancellationRequested();

            for (var i = 0; i < _pluginCreationResults.Count; ++i)
            {
                var result = _pluginCreationResults[i];

                if (!_hasLoggedWarnings && !string.IsNullOrEmpty(result.Message))
                {
                    logger.LogWarning(result.Message);
                }
                else if (result.Claims.Contains(requiredClaim))
                {
                    _hasLoggedWarnings = true;

                    await SetPackageSourceCredentials(result.Plugin, cancellationToken);

                    return result.Plugin;
                }
            }

            return null;
        }

        private async Task SetPackageSourceCredentials(IPlugin plugin, CancellationToken cancellationToken)
        {
            var payload = CreateRequest();

            await plugin.Connection.SendRequestAndReceiveResponseAsync<SetCredentialsRequest, SetCredentialsResponse>(
                MessageMethod.SetPackageSourceCredentials,
                payload,
                cancellationToken);
        }

        private SetCredentialsRequest CreateRequest()
        {
            var sourceUri = _packageSource.SourceUri;
            string proxyUsername = null;
            string proxyPassword = null;
            string username = null;
            string password = null;
            ICredentials credentials;

            if (TryGetCachedCredentials(sourceUri, isProxy: true, credentials: out credentials))
            {
                var proxyCredential = credentials.GetCredential(sourceUri, "Basic");

                if (proxyCredential != null)
                {
                    proxyUsername = proxyCredential.UserName;
                    proxyPassword = proxyCredential.Password;
                }
            }

            if (TryGetCachedCredentials(sourceUri, isProxy: false, credentials: out credentials))
            {
                var packageSourceCredential = credentials.GetCredential(sourceUri, authType: null);

                if (packageSourceCredential != null)
                {
                    username = packageSourceCredential.UserName;
                    password = packageSourceCredential.Password;
                }
            }

            return new SetCredentialsRequest(
                _packageSource.Source,
                proxyUsername,
                proxyPassword,
                username,
                password);
        }

        private static bool TryGetCachedCredentials(Uri uri, bool isProxy, out ICredentials credentials)
        {
            credentials = null;

            var credentialService = HttpHandlerResourceV3.CredentialService;

            if (credentialService == null)
            {
                return false;
            }

            return credentialService.TryGetLastKnownGoodCredentialsFromCache(uri, isProxy, out credentials);
        }
    }
}