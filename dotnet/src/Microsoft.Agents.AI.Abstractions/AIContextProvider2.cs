// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
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
    /// Called during agent invocation to allow the provider to process or modify the request and response.
    /// </summary>
    /// <param name="context">The context for the current request.</param>
    /// <param name="nextProvider">The delegate to invoke the next provider in the pipeline.</param>
    /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask<ResponseContext> InvokeAsync(RequestContext context, Func<RequestContext, CancellationToken, ValueTask<ResponseContext>> nextProvider, CancellationToken cancellationToken = default)
    {
        return await this.InvokeCoreAsync(context, nextProvider, cancellationToken).ConfigureAwait(false);
    }

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
    /// Contains the context information provided to <see cref="InvokeCoreAsync(RequestContext, Func{RequestContext, CancellationToken, ValueTask{ResponseContext}}, CancellationToken)"/>.
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
        /// Gets the caller provided messages that will be used by the agent for this invocation.
        /// </summary>
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
    /// Contains the context information returned from <see cref="InvokeCoreAsync(RequestContext, Func{RequestContext, CancellationToken, ValueTask{ResponseContext}}, CancellationToken)"/>.
    /// </summary>
    public sealed class ResponseContext
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
