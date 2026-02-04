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
/// Provides an abstract base class for components that enhance AI context during agent invocations.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="AIContextProvider2"/> is a component that participates in the agent invocation lifecycle by:
/// <list type="bullet">
/// <item><description>Listening to changes in conversations</description></item>
/// <item><description>Providing additional context to agents during invocation</description></item>
/// <item><description>Supplying additional function tools for enhanced capabilities</description></item>
/// <item><description>Processing invocation results for state management or learning</description></item>
/// </list>
/// </para>
/// </remarks>
public abstract class AIContextProvider2
{
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
    /// Contains the context information provided to <see cref="InvokeCoreAsync(RequestContext, Func{RequestContext, CancellationToken, ValueTask{AgentResponse}}, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="AIContextProvider"/> implementations can set the <see cref="AIContext"/> property provided to add additional context to pass to the ai model during invocation.
    /// </remarks>
    public sealed class RequestContext
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
        /// Gets or sets the caller provided messages that will be used by the agent for this invocation.
        /// </summary>
        /// <remarks>
        /// This can be replaced or updated by the <see cref="AIContextProvider2"/> to add additional messages.
        /// </remarks>
        /// <value>
        /// A collection of <see cref="ChatMessage"/> instances representing new messages that were provided by the caller.
        /// </value>
        public IEnumerable<ChatMessage> RequestMessages { get; set { field = Throw.IfNull(value); } }

        /// <summary>
        /// Gets or sets additional instructions to provide to the AI model for the current invocation.
        /// </summary>
        /// <value>
        /// Instructions text that will be combined with any existing agent instructions or system prompts,
        /// or <see langword="null"/> if no additional instructions should be provided.
        /// </value>
        /// <remarks>
        /// <para>
        /// These instructions are transient and apply only to the current AI model invocation. They are combined
        /// with any existing agent instructions, system prompts, and conversation history to provide comprehensive
        /// context to the AI model.
        /// </para>
        /// <para>
        /// Instructions can be used to:
        /// <list type="bullet">
        /// <item><description>Provide context-specific behavioral guidance</description></item>
        /// <item><description>Add domain-specific knowledge or constraints</description></item>
        /// <item><description>Modify the agent's persona or response style for the current interaction</description></item>
        /// <item><description>Include situational awareness information</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public string? Instructions { get; set; }

        /// <summary>
        /// Gets or sets a collection of tools or functions to make available to the AI model for the current invocation.
        /// </summary>
        /// <value>
        /// A list of <see cref="AITool"/> instances that will be available to the AI model during the current invocation,
        /// or <see langword="null"/> if no additional tools should be provided.
        /// </value>
        /// <remarks>
        /// <para>
        /// These tools are transient and apply only to the current AI model invocation. They are combined with any
        /// tools already configured for the agent to provide an expanded set of capabilities for the specific interaction.
        /// </para>
        /// <para>
        /// Context-specific tools enable:
        /// <list type="bullet">
        /// <item><description>Providing specialized functions based on user intent or conversation context</description></item>
        /// <item><description>Adding domain-specific capabilities for particular types of queries</description></item>
        /// <item><description>Enabling access to external services or data sources relevant to the current task</description></item>
        /// <item><description>Offering interactive capabilities tailored to the current conversation state</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public IList<AITool>? Tools { get; set; }
    }

    /// <summary>
    /// Contains the context information provided to <see cref="InvokeCoreAsync(RequestContext, Func{RequestContext, CancellationToken, ValueTask{AgentResponse}}, CancellationToken)"/>.
    /// </summary>
    public sealed class ResponseContext
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
        public IEnumerable<ChatMessage> ResponseMessages { get; }
    }
}
