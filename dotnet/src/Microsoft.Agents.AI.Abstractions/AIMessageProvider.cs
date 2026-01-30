// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
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
    protected sealed override async ValueTask<AIContextProvider2.ResponseContext> InvokeCoreAsync(AIContextProvider2.RequestContext context, Func<AIContextProvider2.RequestContext, CancellationToken, ValueTask<AIContextProvider2.ResponseContext>> nextProvider, CancellationToken cancellationToken = default)
    {
        RequestContext messagesContext = new(context.Agent, context.Session, context.RequestMessages);

        async ValueTask<ResponseContext> nextProviderAsync(RequestContext msgContext, CancellationToken ct)
        {
            AIContextProvider2.RequestContext aiContextProviderRequestContext = new(msgContext.Agent, msgContext.Session, msgContext.RequestMessages) { Instructions = context.Instructions, Tools = context.Tools };
            var aiContextProviderResponseContext = await base.InvokeCoreAsync(aiContextProviderRequestContext, (c, token) => nextProvider(c, token), ct).ConfigureAwait(false);
            return new ResponseContext(aiContextProviderResponseContext.ResponseMessages);
        }

        var messagesResponse = await this.InvokeAsync(messagesContext, nextProviderAsync, cancellationToken).ConfigureAwait(false);
        return new AIContextProvider2.ResponseContext(messagesResponse.ResponseMessages);
    }

    /// <summary>
    /// Called during agent invocation to allow the provider to process or modify the request and response.
    /// </summary>
    /// <param name="context">The context for the current request.</param>
    /// <param name="nextProvider">The delegate to invoke the next provider in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public ValueTask<ResponseContext> InvokeAsync(RequestContext context, Func<RequestContext, CancellationToken, ValueTask<ResponseContext>> nextProvider, CancellationToken cancellationToken = default)
        => this.InvokeCoreAsync(context, nextProvider, cancellationToken);

    /// <summary>
    /// Called during agent invocation to allow the provider to process or modify the request and response.
    /// </summary>
    /// <param name="context">The context for the current request.</param>
    /// <param name="nextProvider">The delegate to invoke the next provider in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected virtual ValueTask<ResponseContext> InvokeCoreAsync(RequestContext context, Func<RequestContext, CancellationToken, ValueTask<ResponseContext>> nextProvider, CancellationToken cancellationToken = default)
        => nextProvider(context, cancellationToken);

    /// <summary>
    /// Contains the context information provided to <see cref="InvokeAsync(RequestContext, Func{RequestContext, CancellationToken, ValueTask{ResponseContext}}, CancellationToken)"/>.
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
    /// Contains the context information returned from <see cref="InvokeAsync(RequestContext, Func{RequestContext, CancellationToken, ValueTask{ResponseContext}}, CancellationToken)"/>.
    /// </summary>
    public new sealed class ResponseContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResponseContext"/> class with the specified response messages.
        /// </summary>
        /// <param name="responseMessages">The collection of response messages generated during this invocation.</param>
        public ResponseContext(IEnumerable<ChatMessage> responseMessages)
        {
            this.ResponseMessages = Throw.IfNull(responseMessages);
        }

        /// <summary>
        /// Gets the collection of response messages generated during this invocation if the invocation succeeded.
        /// </summary>
        public IEnumerable<ChatMessage> ResponseMessages { get; set; }
    }
}
