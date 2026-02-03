// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

using ProviderInitializer = System.Func<Microsoft.Agents.AI.SampleAIContextProvider.MyState?, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask<Microsoft.Agents.AI.SampleAIContextProvider.MyState?>>;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides a sample implementation of an AI context provider that manages state within an agent session.
/// </summary>
public class SampleAIContextProvider : AIContextProvider2
{
    private readonly string _stateKey;
    private readonly ProviderInitializer? _providerStateInitializer;

    /// <summary>
    /// Initializes a new instance of the SampleAIContextProvider class.
    /// </summary>
    /// <param name="stateKey">An optional key used to identify the context provider's state. If null, the provider's type name is used as the
    /// default key.</param>
    /// <param name="providerStateInitializer">An optional delegate that initializes the provider's state when the context is created.</param>
    public SampleAIContextProvider(string? stateKey = null, ProviderInitializer? providerStateInitializer = null)
    {
        this._stateKey = stateKey ?? this.GetType().FullName!;
        this._providerStateInitializer = providerStateInitializer;
    }

    /// <inheritdoc/>
    protected override async ValueTask<AgentResponse> InvokeCoreAsync(RequestContext context, Func<RequestContext, CancellationToken, ValueTask<AgentResponse>> nextProvider, CancellationToken cancellationToken = default)
    {
        MyState? providerState = null;
        if (context.Session != null)
        {
            context.Session.StateBag.TryGetValue(this._stateKey, out providerState);
        }

        if (this._providerStateInitializer is not null)
        {
            providerState = await this._providerStateInitializer.Invoke(providerState, cancellationToken).ConfigureAwait(false);
        }

        var response = await nextProvider(context, cancellationToken).ConfigureAwait(false);

        if (context.Session != null && providerState != null)
        {
            context.Session.StateBag.SetValue(this._stateKey, providerState);
        }

        return response;
    }

    /// <summary>
    /// Represents the state required by the <see cref="SampleAIContextProvider"/> class to function correctly.
    /// </summary>
    public class MyState
    {
        /// <summary>
        /// Gets or sets the data associated with this instance.
        /// </summary>
        public string Data { get; set; } = string.Empty;
    }
}
