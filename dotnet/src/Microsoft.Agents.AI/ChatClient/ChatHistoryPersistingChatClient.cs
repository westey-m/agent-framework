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
/// A delegating chat client that notifies <see cref="ChatHistoryProvider"/> and <see cref="AIContextProvider"/>
/// instances of request and response messages after each individual call to the inner chat client.
/// </summary>
/// <remarks>
/// <para>
/// This decorator is intended to operate between the <see cref="FunctionInvokingChatClient"/> and the leaf
/// <see cref="IChatClient"/> in a <see cref="ChatClientAgent"/> pipeline. It ensures that providers are notified
/// after each service call rather than only at the end of the full agent run, so that intermediate messages
/// (e.g., tool calls and results) are saved even if the process is interrupted mid-loop.
/// </para>
/// <para>
/// This chat client must be used within the context of a running <see cref="ChatClientAgent"/>. It retrieves the
/// current agent and session from <see cref="AIAgent.CurrentRunContext"/>, which is set automatically when an agent's
/// <see cref="AIAgent.RunAsync(IEnumerable{ChatMessage}, AgentSession?, AgentRunOptions?, CancellationToken)"/> or
/// <see cref="AIAgent.RunStreamingAsync(IEnumerable{ChatMessage}, AgentSession?, AgentRunOptions?, CancellationToken)"/>
/// method is called. An <see cref="InvalidOperationException"/> is thrown if no run context is available or if the
/// agent is not a <see cref="ChatClientAgent"/>.
/// </para>
/// </remarks>
internal sealed class ChatHistoryPersistingChatClient : DelegatingChatClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatHistoryPersistingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The underlying chat client that will handle the core operations.</param>
    public ChatHistoryPersistingChatClient(IChatClient innerClient)
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

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var newRequestMessagesOnFailure = GetNewMessages(messages, session);
            MarkAsNotified(newRequestMessagesOnFailure, session);
            await agent.NotifyProvidersOfFailureAsync(session, ex, newRequestMessagesOnFailure, options, cancellationToken).ConfigureAwait(false);
            throw;
        }

        var newRequestMessages = GetNewMessages(messages, session);
        MarkAsNotified(newRequestMessages, session);
        MarkAsNotified(response.Messages, session);
        await agent.NotifyProvidersOfNewMessagesAsync(session, newRequestMessages, response.Messages, options, cancellationToken).ConfigureAwait(false);

        return response;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (agent, session) = GetRequiredAgentAndSession();

        List<ChatResponseUpdate> responseUpdates = [];

        IAsyncEnumerator<ChatResponseUpdate> enumerator;
        try
        {
            enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            var newRequestMessagesOnFailure = GetNewMessages(messages, session);
            MarkAsNotified(newRequestMessagesOnFailure, session);
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
            var newRequestMessagesOnFailure = GetNewMessages(messages, session);
            MarkAsNotified(newRequestMessagesOnFailure, session);
            await agent.NotifyProvidersOfFailureAsync(session, ex, newRequestMessagesOnFailure, options, cancellationToken).ConfigureAwait(false);
            throw;
        }

        while (hasUpdates)
        {
            var update = enumerator.Current;
            responseUpdates.Add(update);
            yield return update;

            try
            {
                hasUpdates = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var newRequestMessagesOnFailure = GetNewMessages(messages, session);
                MarkAsNotified(newRequestMessagesOnFailure, session);
                await agent.NotifyProvidersOfFailureAsync(session, ex, newRequestMessagesOnFailure, options, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        var chatResponse = responseUpdates.ToChatResponse();
        var newRequestMessages = GetNewMessages(messages, session);
        MarkAsNotified(newRequestMessages, session);
        MarkAsNotified(chatResponse.Messages, session);
        await agent.NotifyProvidersOfNewMessagesAsync(session, newRequestMessages, chatResponse.Messages, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the current <see cref="ChatClientAgent"/> and <see cref="ChatClientAgentSession"/> from the run context.
    /// </summary>
    private static (ChatClientAgent Agent, ChatClientAgentSession Session) GetRequiredAgentAndSession()
    {
        var runContext = AIAgent.CurrentRunContext
            ?? throw new InvalidOperationException(
                $"{nameof(ChatHistoryPersistingChatClient)} can only be used within the context of a running AIAgent. " +
                "Ensure that the chat client is being invoked as part of an AIAgent.RunAsync or AIAgent.RunStreamingAsync call.");

        if (runContext.Agent is not ChatClientAgent chatClientAgent)
        {
            throw new InvalidOperationException(
                $"{nameof(ChatHistoryPersistingChatClient)} can only be used with a {nameof(ChatClientAgent)}. " +
                $"The current agent is of type '{runContext.Agent.GetType().Name}'.");
        }

        if (runContext.Session is not ChatClientAgentSession chatClientAgentSession)
        {
            throw new InvalidOperationException(
                $"{nameof(ChatHistoryPersistingChatClient)} requires a {nameof(ChatClientAgentSession)}. " +
                $"The current session is of type '{runContext.Session?.GetType().Name ?? "null"}'.");
        }

        return (chatClientAgent, chatClientAgentSession);
    }

    /// <summary>
    /// Filters the given messages to return only those that have not yet been notified to providers
    /// during the current agent run.
    /// </summary>
    /// <param name="messages">The full set of messages to filter.</param>
    /// <param name="session">The current session containing the set of already-notified messages.</param>
    /// <returns>A list of messages that have not yet been notified. If no tracking is available, all messages are returned.</returns>
    private static IReadOnlyList<ChatMessage> GetNewMessages(IEnumerable<ChatMessage> messages, ChatClientAgentSession session)
    {
        HashSet<ChatMessage>? notifiedMessages = session.NotifiedMessages;
        if (notifiedMessages is null or { Count: 0 })
        {
            return messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        }

        return messages.Where(m => !notifiedMessages.Contains(m)).ToList();
    }

    /// <summary>
    /// Marks the given messages as notified so they will be excluded from future notifications in the current run.
    /// </summary>
    /// <param name="messages">The messages to mark as notified.</param>
    /// <param name="session">The current session containing the set of already-notified messages.</param>
    private static void MarkAsNotified(IEnumerable<ChatMessage> messages, ChatClientAgentSession session)
    {
        if (session.NotifiedMessages is { } notifiedMessages)
        {
            foreach (var message in messages)
            {
                notifiedMessages.Add(message);
            }
        }
    }
}
