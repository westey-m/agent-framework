// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using ProviderInitializer = System.Func<Microsoft.Agents.AI.SampleAIMessageProvider.MyState?, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask<Microsoft.Agents.AI.SampleAIMessageProvider.MyState?>>;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides a sample implementation of an AI message provider that manages state within an agent session.
/// </summary>
public class SampleAIMessageProvider : AIMessageProvider
{
    private readonly string _stateKey;
    private readonly ProviderInitializer? _providerStateInitializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SampleAIMessageProvider"/> class.
    /// </summary>
    /// <param name="stateKey">An optional key used to identify the message provider's state. If null, the provider's type name is used as the
    /// default key.</param>
    /// <param name="providerStateInitializer">An optional delegate that initializes the provider's state when the context is created.</param>
    public SampleAIMessageProvider(string? stateKey = null, ProviderInitializer? providerStateInitializer = null)
    {
        this._stateKey = stateKey ?? this.GetType().FullName!;
        this._providerStateInitializer = providerStateInitializer;
    }

    /// <inheritdoc/>
    public override async ValueTask<RequestContext> InvokingAsync(RequestContext context, CancellationToken cancellationToken = default)
    {
        // Check the state bag for our state.
        MyState? providerState = null;
        context.Session?.StateBag.TryGetValue(this._stateKey, out providerState);

        // Initialize the state if it's null.
        if (this._providerStateInitializer is not null)
        {
            providerState = await this._providerStateInitializer.Invoke(providerState, cancellationToken).ConfigureAwait(false);
        }

        // Store the initialized state back in the state bag.
        if (context.Session != null && providerState != null)
        {
            context.Session.StateBag.SetValue(this._stateKey, providerState);
        }

        // Add the state data to the request messages.
        context.RequestMessages = context.RequestMessages.Concat([new ChatMessage(ChatRole.User, providerState?.Data ?? string.Empty)]);
        return context;
    }

    /// <inheritdoc/>
    public override ValueTask InvokedAsync(ResponseContext? context, Exception? invokeException, CancellationToken cancellationToken = default)
    {
        if (invokeException != null)
        {
            return default;
        }

        // Extract the final message to update the state.
        if (context?.ResponseMessages != null && context.Session != null)
        {
            var responseMessage = context.ResponseMessages.LastOrDefault();
            if (responseMessage != null)
            {
                MyState providerState = new() { Data = responseMessage.Text };
                context.Session.StateBag.SetValue(this._stateKey, providerState);
            }
        }

        return default;
    }

    /// <inheritdoc/>
    protected override async ValueTask<AgentResponse> InvokeCoreAsync(RequestContext context, Func<RequestContext, CancellationToken, ValueTask<AgentResponse>> nextProvider, CancellationToken cancellationToken = default)
    {
        // Check the state bag for our state.
        MyState? providerState = null;
        context.Session?.StateBag.TryGetValue(this._stateKey, out providerState);

        // Initialize the state if it's null.
        if (this._providerStateInitializer is not null)
        {
            providerState = await this._providerStateInitializer.Invoke(providerState, cancellationToken).ConfigureAwait(false);
        }

        // Store the initialized state back in the state bag.
        if (context.Session != null && providerState != null)
        {
            context.Session.StateBag.SetValue(this._stateKey, providerState);
        }

        // Add the state data to the request messages.
        context.RequestMessages = context.RequestMessages.Concat([new ChatMessage(ChatRole.User, providerState?.Data ?? string.Empty)]);

        // Call the next provider in the chain.
        var response = await nextProvider(context, cancellationToken).ConfigureAwait(false);

        // Extract the final message to update the state.
        if (response.Messages != null && context.Session != null)
        {
            var responseMessage = response.Messages.LastOrDefault();
            if (responseMessage != null)
            {
                providerState = new() { Data = responseMessage.Text };
                context.Session.StateBag.SetValue(this._stateKey, providerState);
            }
        }

        return response;
    }

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<AgentResponseUpdate> InvokeCoreStreamingAsync(RequestContext context, Func<RequestContext, CancellationToken, IAsyncEnumerable<AgentResponseUpdate>> nextProvider, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Check the state bag for our state.
        MyState? providerState = null;
        context.Session?.StateBag.TryGetValue(this._stateKey, out providerState);

        // Initialize the state if it's null.
        if (this._providerStateInitializer is not null)
        {
            providerState = await this._providerStateInitializer.Invoke(providerState, cancellationToken).ConfigureAwait(false);
        }

        // Store the initialized state back in the state bag.
        if (context.Session != null && providerState != null)
        {
            context.Session.StateBag.SetValue(this._stateKey, providerState);
        }

        // Add the state data to the request messages.
        context.RequestMessages = context.RequestMessages.Concat([new ChatMessage(ChatRole.User, providerState?.Data ?? string.Empty)]);

        // Call the next provider in the chain.
        var responseUpdatesStream = nextProvider(context, cancellationToken);

        // Stream the response updates and collect them.
        var responseUpdates = new List<AgentResponseUpdate>();
        await foreach (var update in responseUpdatesStream.ConfigureAwait(false))
        {
            responseUpdates.Add(update);
            yield return update;
        }

        // Extract the final message to update the state.
        var agentResponse = responseUpdates.ToAgentResponse();
        if (agentResponse.Messages != null && context.Session != null)
        {
            var responseMessage = agentResponse.Messages.LastOrDefault();
            if (responseMessage != null)
            {
                providerState = new() { Data = responseMessage.Text };
                context.Session.StateBag.SetValue(this._stateKey, providerState);
            }
        }
    }

    /// <summary>
    /// Represents the state required by the <see cref="SampleAIMessageProvider"/> class to function correctly.
    /// </summary>
    public class MyState
    {
        /// <summary>
        /// Gets or sets the data associated with this instance.
        /// </summary>
        public string Data { get; set; } = string.Empty;
    }
}
