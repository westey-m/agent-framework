// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides an abstract base class for components that produce additional messagtes during agent invocations.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="AIMessageProvider"/> is a component that participates in the agent invocation lifecycle by:
/// <list type="bullet">
/// <item><description>Listening to changes in conversations</description></item>
/// <item><description>Providing additional messages to agents during invocation</description></item>
/// </list>
/// </para>
/// </remarks>
public class AIMessageProvider : AIContextProvider2
{
    /// <inheritdoc/>
    public override sealed ValueTask<AIContextProvider2.RequestContext> InvokingAsync(AIContextProvider2.RequestContext context, CancellationToken cancellationToken = default)
        => base.InvokingAsync(context, cancellationToken);

    /// <inheritdoc/>
    public override sealed ValueTask InvokedAsync(AIContextProvider2.ResponseContext? context, Exception? invokeException, CancellationToken cancellationToken = default)
        => base.InvokedAsync(context, invokeException, cancellationToken);

    /// <summary>
    /// Called at the start of agent invocation to provide additional context.
    /// </summary>
    /// <param name="context">Contains the request context including the caller provided messages that will be used by the agent for this invocation.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the <see cref="AIContext"/> with additional context to be used by the agent during this invocation.</returns>
    /// <remarks>
    /// <para>
    /// Implementers can load any additional context required at this time, such as:
    /// <list type="bullet">
    /// <item><description>Retrieving relevant information from knowledge bases</description></item>
    /// <item><description>Adding system instructions or prompts</description></item>
    /// <item><description>Providing function tools for the current invocation</description></item>
    /// <item><description>Injecting contextual messages from conversation history</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public virtual ValueTask<RequestContext> InvokingAsync(RequestContext context, CancellationToken cancellationToken = default)
        => new(context);

    /// <summary>
    /// Called at the end of the agent invocation to process the invocation results.
    /// </summary>
    /// <param name="context">Contains the invocation context including request messages, response messages, and any exception that occurred.</param>
    /// <param name="invokeException">The <see cref="Exception"/> that was thrown during the invocation, if the invocation failed; otherwise, <see langword="null"/>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>
    /// <para>
    /// Implementers can use the request and response messages in the provided <paramref name="context"/> to:
    /// <list type="bullet">
    /// <item><description>Update internal state based on conversation outcomes</description></item>
    /// <item><description>Extract and store memories or preferences from user messages</description></item>
    /// <item><description>Log or audit conversation details</description></item>
    /// <item><description>Perform cleanup or finalization tasks</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// This method is called regardless of whether the invocation succeeded or failed.
    /// To check if the invocation was successful, inspect the <paramref name="invokeException"/> property.
    /// </para>
    /// </remarks>
    public virtual ValueTask InvokedAsync(ResponseContext? context, Exception? invokeException, CancellationToken cancellationToken = default)
        => default;

    /// <inheritdoc/>
    protected sealed override async ValueTask<AgentResponse> InvokeCoreAsync(AIContextProvider2.RequestContext context, Func<AIContextProvider2.RequestContext, CancellationToken, ValueTask<AgentResponse>> nextProvider, CancellationToken cancellationToken = default)
    {
        RequestContext messagesContext = new(context.Agent, context.Session, context.RequestMessages);

        async ValueTask<AgentResponse> nextProviderAsync(RequestContext msgContext, CancellationToken ct)
        {
            AIContextProvider2.RequestContext aiContextProviderRequestContext = new(msgContext.Agent, msgContext.Session, msgContext.RequestMessages) { Instructions = context.Instructions, Tools = context.Tools };
            return await base.InvokeCoreAsync(aiContextProviderRequestContext, (c, token) => nextProvider(c, token), ct).ConfigureAwait(false);
        }

        return await this.InvokeAsync(messagesContext, nextProviderAsync, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Called during agent invocation to allow the provider to process or modify the request and response.
    /// </summary>
    /// <param name="context">The context for the current request.</param>
    /// <param name="nextProvider">The delegate to invoke the next provider in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask<AgentResponse> InvokeAsync(RequestContext context, Func<RequestContext, CancellationToken, ValueTask<AgentResponse>> nextProvider, CancellationToken cancellationToken = default)
    {
        context = await this.InvokingAsync(context, cancellationToken).ConfigureAwait(false);

        AgentResponse? response;
        try
        {
            response = await this.InvokeCoreAsync(context, nextProvider, cancellationToken).ConfigureAwait(false);
            var responseContext = new ResponseContext(context.Agent, context.Session, response.Messages);
            await this.InvokedAsync(responseContext, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await this.InvokedAsync(null, ex, cancellationToken).ConfigureAwait(false);
            throw;
        }

        return response;
    }

    /// <summary>
    /// Called during agent invocation to allow the provider to process or modify the request and response.
    /// </summary>
    /// <param name="context">The context for the current request.</param>
    /// <param name="nextProvider">The delegate to invoke the next provider in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected virtual ValueTask<AgentResponse> InvokeCoreAsync(RequestContext context, Func<RequestContext, CancellationToken, ValueTask<AgentResponse>> nextProvider, CancellationToken cancellationToken = default)
        => nextProvider(context, cancellationToken);

    /// <inheritdoc/>
    protected sealed override IAsyncEnumerable<AgentResponseUpdate> InvokeCoreStreamingAsync(AIContextProvider2.RequestContext context, Func<AIContextProvider2.RequestContext, CancellationToken, IAsyncEnumerable<AgentResponseUpdate>> nextProvider, CancellationToken cancellationToken = default)
    {
        RequestContext messagesContext = new(context.Agent, context.Session, context.RequestMessages);

        IAsyncEnumerable<AgentResponseUpdate> nextProviderAsync(RequestContext msgContext, CancellationToken ct)
        {
            AIContextProvider2.RequestContext aiContextProviderRequestContext = new(msgContext.Agent, msgContext.Session, msgContext.RequestMessages) { Instructions = context.Instructions, Tools = context.Tools };
            return base.InvokeCoreStreamingAsync(aiContextProviderRequestContext, (c, token) => nextProvider(c, token), ct);
        }

        return this.InvokeStreamingAsync(messagesContext, nextProviderAsync, cancellationToken);
    }

    /// <summary>
    /// Called during agent invocation to allow the provider to process or modify the request and response.
    /// </summary>
    /// <param name="context">The context for the current request.</param>
    /// <param name="nextProvider">The delegate to invoke the next provider in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async IAsyncEnumerable<AgentResponseUpdate> InvokeStreamingAsync(RequestContext context, Func<RequestContext, CancellationToken, IAsyncEnumerable<AgentResponseUpdate>> nextProvider, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        context = await this.InvokingAsync(context, cancellationToken).ConfigureAwait(false);

        List<AgentResponseUpdate> responseUpdates = new();
        IAsyncEnumerator<AgentResponseUpdate> responseUpdatesEnumerator;

        try
        {
            // Using the enumerator to ensure we consider the case where no updates are returned for notification.
            responseUpdatesEnumerator = this.InvokeCoreStreamingAsync(context, nextProvider, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            await this.InvokedAsync(null, ex, cancellationToken).ConfigureAwait(false);
            throw;
        }

        bool hasUpdates;
        try
        {
            // Ensure we start the streaming request
            hasUpdates = await responseUpdatesEnumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await this.InvokedAsync(null, ex, cancellationToken).ConfigureAwait(false);
            throw;
        }

        while (hasUpdates)
        {
            var update = responseUpdatesEnumerator.Current;
            if (update is not null)
            {
                yield return update;
            }

            try
            {
                hasUpdates = await responseUpdatesEnumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await this.InvokedAsync(null, ex, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        var agentResponse = responseUpdates.ToAgentResponse();

        await this.InvokedAsync(new(context.Agent, context.Session, agentResponse.Messages), null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Called during agent invocation to allow the provider to process or modify the request and response.
    /// </summary>
    /// <param name="context">The context for the current request.</param>
    /// <param name="nextProvider">The delegate to invoke the next provider in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected virtual IAsyncEnumerable<AgentResponseUpdate> InvokeCoreStreamingAsync(RequestContext context, Func<RequestContext, CancellationToken, IAsyncEnumerable<AgentResponseUpdate>> nextProvider, CancellationToken cancellationToken = default)
        => nextProvider(context, cancellationToken);

    /// <summary>
    /// Contains the context information provided to <see cref="InvokeAsync(RequestContext, Func{RequestContext, CancellationToken, ValueTask{AgentResponse}}, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="AIContextProvider"/> implementations can set the <see cref="AIContext"/> property provided to add additional context to pass to the ai model during invocation.
    /// </remarks>
    public new sealed class RequestContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RequestContext"/> class with the specified request messages.
        /// </summary>
        /// <param name="agent">The agent being invoked.</param>
        /// <param name="session">The session associated with the agent invocation.</param>
        /// <param name="requestMessages">The messages to be used by the agent for this invocation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="requestMessages"/> is <see langword="null"/>.</exception>
        public RequestContext(
            AIAgent agent,
            AgentSession? session,
            IEnumerable<ChatMessage> requestMessages)
        {
            this.Agent = Throw.IfNull(agent);
            this.Session = session;
            this.RequestMessages = Throw.IfNull(requestMessages);
        }

        /// <summary>
        /// Gets the AI agent associated with this instance.
        /// </summary>
        public AIAgent Agent { get; }

        /// <summary>
        /// Gets the current agent session associated with this instance.
        /// </summary>
        public AgentSession? Session { get; }

        /// <summary>
        /// Gets the caller provided messages that will be used by the agent for this invocation.
        /// </summary>
        /// <value>
        /// A collection of <see cref="ChatMessage"/> instances representing new messages that were provided by the caller.
        /// </value>
        public IEnumerable<ChatMessage> RequestMessages { get; set { field = Throw.IfNull(value); } }
    }

    /// <summary>
    /// Contains the context information provided to <see cref="InvokedAsync(ResponseContext?, Exception?, CancellationToken)"/>.
    /// </summary>
    public new sealed class ResponseContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResponseContext"/> class with the specified response messages.
        /// </summary>
        /// <param name="agent">The agent being invoked.</param>
        /// <param name="session">The session associated with the agent invocation.</param>
        /// <param name="responseMessages">The collection of response messages generated during this invocation.</param>
        public ResponseContext(
            AIAgent agent,
            AgentSession? session,
            IEnumerable<ChatMessage> responseMessages)
        {
            this.Agent = Throw.IfNull(agent);
            this.Session = session;
            this.ResponseMessages = Throw.IfNull(responseMessages);
        }

        /// <summary>
        /// Gets the AI agent associated with this instance.
        /// </summary>
        public AIAgent Agent { get; }

        /// <summary>
        /// Gets the current agent session associated with this instance.
        /// </summary>
        public AgentSession? Session { get; }

        /// <summary>
        /// Gets the collection of response messages generated during this invocation if the invocation succeeded.
        /// </summary>
        public IEnumerable<ChatMessage> ResponseMessages { get; set; }
    }
}
