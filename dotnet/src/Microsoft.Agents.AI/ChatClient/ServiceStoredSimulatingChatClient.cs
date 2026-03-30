// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// A delegating chat client that simulates service-stored chat history behavior using
/// framework-managed <see cref="ChatHistoryProvider"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// This decorator is intended to operate between the <see cref="FunctionInvokingChatClient"/> and the leaf
/// <see cref="IChatClient"/> in a <see cref="ChatClientAgent"/> pipeline.
/// </para>
/// <para>
/// Before each service call, it loads chat history from the agent's <see cref="ChatHistoryProvider"/>
/// and prepends it to the request messages. After each successful service call, it persists
/// new request and response messages to the provider. It also returns a sentinel
/// <see cref="ChatOptions.ConversationId"/> on the response so that the
/// <see cref="FunctionInvokingChatClient"/> treats the conversation as service-managed —
/// clearing accumulated history between iterations and not injecting duplicate
/// <see cref="FunctionCallContent"/> during approval-response processing.
/// </para>
/// <para>
/// This chat client must be used within the context of a running <see cref="ChatClientAgent"/>. It retrieves the
/// current agent and session from <see cref="AIAgent.CurrentRunContext"/>, which is set automatically when an agent's
/// <see cref="AIAgent.RunAsync(IEnumerable{ChatMessage}, AgentSession?, AgentRunOptions?, CancellationToken)"/> or
/// <see cref="AIAgent.RunStreamingAsync(IEnumerable{ChatMessage}, AgentSession?, AgentRunOptions?, CancellationToken)"/>
/// method is called. The <see cref="ChatClientAgent"/> ensures the run context always contains a resolved session,
/// even when the caller passes null. An <see cref="InvalidOperationException"/> is thrown if no run context is
/// available or if the agent is not a <see cref="ChatClientAgent"/>.
/// </para>
/// </remarks>
internal sealed class ServiceStoredSimulatingChatClient : DelegatingChatClient
{
    /// <summary>
    /// A sentinel value returned on <see cref="ChatResponse.ConversationId"/> to signal
    /// <see cref="FunctionInvokingChatClient"/> that chat history is being managed downstream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="FunctionInvokingChatClient"/> sees a non-null <see cref="ChatResponse.ConversationId"/>,
    /// it treats the conversation as service-managed: it clears accumulated history between
    /// iterations (via <c>FixupHistories</c>) and does not inject <see cref="FunctionCallContent"/>
    /// into the request during approval-response processing (via <c>ProcessFunctionApprovalResponses</c>).
    /// </para>
    /// <para>
    /// This decorator strips the sentinel from <see cref="ChatOptions.ConversationId"/> on incoming
    /// requests before forwarding to the inner client, so the underlying model never sees it.
    /// </para>
    /// </remarks>
    internal const string LocalHistoryConversationId = "_agent_local_chat_history";

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceStoredSimulatingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The underlying chat client that will handle the core operations.</param>
    public ServiceStoredSimulatingChatClient(IChatClient innerClient)
        : base(innerClient)
    {
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (agent, session) = GetRequiredAgentAndSession();
        options = StripLocalHistoryConversationId(options);

        // Load history and prepend it to the messages.
        var allMessages = await agent.LoadChatHistoryAsync(session, messages, options, cancellationToken).ConfigureAwait(false);

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(allMessages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var newRequestMessages = GetNewRequestMessages(allMessages);
            await agent.NotifyProvidersOfFailureAsync(session, ex, newRequestMessages, options, cancellationToken).ConfigureAwait(false);
            throw;
        }

        var newMessages = GetNewRequestMessages(allMessages);

        // Persist immediately after each service call.
        await agent.NotifyProvidersOfNewMessagesAsync(session, newMessages, response.Messages, options, cancellationToken).ConfigureAwait(false);

        // Set the sentinel ConversationId on the response and session.
        SetSentinelConversationId(response, session);

        return response;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (agent, session) = GetRequiredAgentAndSession();
        options = StripLocalHistoryConversationId(options);

        // Load history and prepend it to the messages.
        var allMessages = await agent.LoadChatHistoryAsync(session, messages, options, cancellationToken).ConfigureAwait(false);

        List<ChatResponseUpdate> responseUpdates = [];

        IAsyncEnumerator<ChatResponseUpdate> enumerator;
        try
        {
            enumerator = base.GetStreamingResponseAsync(allMessages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            var newRequestMessagesOnFailure = GetNewRequestMessages(allMessages);
            await agent.NotifyProvidersOfFailureAsync(session, ex, newRequestMessagesOnFailure, options, cancellationToken).ConfigureAwait(false);
            throw;
        }

        bool hasUpdates;
        try
        {
            hasUpdates = await enumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var newRequestMessagesOnFailure = GetNewRequestMessages(allMessages);
            await agent.NotifyProvidersOfFailureAsync(session, ex, newRequestMessagesOnFailure, options, cancellationToken).ConfigureAwait(false);
            throw;
        }

        while (hasUpdates)
        {
            var update = enumerator.Current;
            responseUpdates.Add(update);
            update.ConversationId = LocalHistoryConversationId; // Set the sentinel ConversationId on each update for the streaming case.
            yield return update;

            try
            {
                hasUpdates = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var newRequestMessagesOnFailure = GetNewRequestMessages(allMessages);
                await agent.NotifyProvidersOfFailureAsync(session, ex, newRequestMessagesOnFailure, options, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        var chatResponse = responseUpdates.ToChatResponse();
        var newMessages = GetNewRequestMessages(allMessages);

        // Persist immediately after each service call.
        await agent.NotifyProvidersOfNewMessagesAsync(session, newMessages, chatResponse.Messages, options, cancellationToken).ConfigureAwait(false);

        // Set the sentinel ConversationId on the session. For streaming, set it on the last update.
        session.ConversationId = LocalHistoryConversationId;
    }

    /// <summary>
    /// Sets the sentinel <see cref="LocalHistoryConversationId"/> on the response and session
    /// so that <see cref="FunctionInvokingChatClient"/> treats the conversation as service-managed.
    /// </summary>
    private static void SetSentinelConversationId(ChatResponse response, ChatClientAgentSession session)
    {
        response.ConversationId = LocalHistoryConversationId;
        session.ConversationId = LocalHistoryConversationId;
    }

    /// <summary>
    /// Gets the current <see cref="ChatClientAgent"/> and <see cref="ChatClientAgentSession"/> from the run context.
    /// </summary>
    private static (ChatClientAgent Agent, ChatClientAgentSession Session) GetRequiredAgentAndSession()
    {
        var runContext = AIAgent.CurrentRunContext
            ?? throw new InvalidOperationException(
                $"{nameof(ServiceStoredSimulatingChatClient)} can only be used within the context of a running AIAgent. " +
                "Ensure that the chat client is being invoked as part of an AIAgent.RunAsync or AIAgent.RunStreamingAsync call.");

        var chatClientAgent = runContext.Agent.GetService<ChatClientAgent>()
            ?? throw new InvalidOperationException(
                $"{nameof(ServiceStoredSimulatingChatClient)} can only be used with a {nameof(ChatClientAgent)}. " +
                $"The current agent is of type '{runContext.Agent.GetType().Name}'.");

        if (runContext.Session is not ChatClientAgentSession chatClientAgentSession)
        {
            throw new InvalidOperationException(
                $"{nameof(ServiceStoredSimulatingChatClient)} requires a {nameof(ChatClientAgentSession)}. " +
                $"The current session is of type '{runContext.Session?.GetType().Name ?? "null"}'.");
        }

        return (chatClientAgent, chatClientAgentSession);
    }

    /// <summary>
    /// Returns only the request messages that have not been loaded from chat history.
    /// </summary>
    /// <remarks>
    /// Messages loaded by the <see cref="ChatHistoryProvider"/> are tagged with
    /// <see cref="AgentRequestMessageSourceType.ChatHistory"/> and should not be re-persisted.
    /// Because <see cref="FunctionInvokingChatClient"/> treats the conversation as service-managed
    /// (via the sentinel <see cref="LocalHistoryConversationId"/>), it clears accumulated history
    /// between iterations, so only genuinely new messages arrive here.
    /// </remarks>
    /// <param name="messages">The full set of request messages to filter.</param>
    /// <returns>A list of request messages that need to be persisted.</returns>
    private static List<ChatMessage> GetNewRequestMessages(IEnumerable<ChatMessage> messages)
    {
        return messages.Where(m => m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.ChatHistory).ToList();
    }

    /// <summary>
    /// If the <paramref name="options"/> carry the <see cref="LocalHistoryConversationId"/> sentinel,
    /// returns a clone with the conversation ID cleared so the inner client never sees it.
    /// Otherwise returns the original <paramref name="options"/> unchanged.
    /// </summary>
    private static ChatOptions? StripLocalHistoryConversationId(ChatOptions? options)
    {
        if (options?.ConversationId == LocalHistoryConversationId)
        {
            options = options.Clone();
            options.ConversationId = null;
        }

        return options;
    }
}
