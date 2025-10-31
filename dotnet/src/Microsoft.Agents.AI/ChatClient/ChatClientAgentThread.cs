﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides a thread implementation for use with <see cref="ChatClientAgent"/>.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public class ChatClientAgentThread : AgentThread
{
    private string? _conversationId;
    private ChatMessageStore? _messageStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatClientAgentThread"/> class.
    /// </summary>
    internal ChatClientAgentThread()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatClientAgentThread"/> class from previously serialized state.
    /// </summary>
    /// <param name="serializedThreadState">A <see cref="JsonElement"/> representing the serialized state of the thread.</param>
    /// <param name="jsonSerializerOptions">Optional settings for customizing the JSON deserialization process.</param>
    /// <param name="chatMessageStoreFactory">
    /// An optional factory function to create a custom <see cref="ChatMessageStore"/> from its serialized state.
    /// If not provided, the default in-memory message store will be used.
    /// </param>
    /// <param name="aiContextProviderFactory">
    /// An optional factory function to create a custom <see cref="AIContextProvider"/> from its serialized state.
    /// If not provided, no context provider will be configured.
    /// </param>
    internal ChatClientAgentThread(
        JsonElement serializedThreadState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        Func<JsonElement, JsonSerializerOptions?, ChatMessageStore>? chatMessageStoreFactory = null,
        Func<JsonElement, JsonSerializerOptions?, AIContextProvider>? aiContextProviderFactory = null)
    {
        if (serializedThreadState.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The serialized thread state must be a JSON object.", nameof(serializedThreadState));
        }

        var state = serializedThreadState.Deserialize(
            AgentJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ThreadState))) as ThreadState;

        this.AIContextProvider = aiContextProviderFactory?.Invoke(state?.AIContextProviderState ?? default, jsonSerializerOptions);

        if (state?.ConversationId is string threadId)
        {
            this.ConversationId = threadId;

            // Since we have an ID, we should not have a chat message store and we can return here.
            return;
        }

        this._messageStore =
            chatMessageStoreFactory?.Invoke(state?.StoreState ?? default, jsonSerializerOptions) ??
            new InMemoryChatMessageStore(state?.StoreState ?? default, jsonSerializerOptions); // default to an in-memory store
    }

    /// <summary>
    /// Gets or sets the ID of the underlying service thread to support cases where the chat history is stored by the agent service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Note that either <see cref="ConversationId"/> or <see cref="MessageStore "/> may be set, but not both.
    /// If <see cref="MessageStore "/> is not null, and <see cref="ConversationId"/> is set, <see cref="MessageStore "/>
    /// will be reverted to null, and vice versa.
    /// </para>
    /// <para>
    /// This property may be null in the following cases:
    /// <list type="bullet">
    /// <item><description>The thread stores messages via the <see cref="ChatMessageStore"/> and not in the agent service.</description></item>
    /// <item><description>This thread object is new and a server managed thread has not yet been created in the agent service.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The id may also change over time where the id is pointing at a
    /// agent service managed thread, and the default behavior of a service is
    /// to fork the thread with each iteration.
    /// </para>
    /// </remarks>
    public string? ConversationId
    {
        get => this._conversationId;
        internal set
        {
            if (string.IsNullOrWhiteSpace(this._conversationId) && string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (this._messageStore is not null)
            {
                // If we have a message store already, we shouldn't switch the thread to use a conversation id
                // since it means that the thread contents will essentially be deleted, and the thread will not work
                // with the original agent anymore.
                throw new InvalidOperationException("Only the ConversationId or MessageStore may be set, but not both and switching from one to another is not supported.");
            }

            this._conversationId = Throw.IfNullOrWhitespace(value);
        }
    }

    /// <summary>
    /// Gets or sets the <see cref="ChatMessageStore"/> used by this thread, for cases where messages should be stored in a custom location.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Note that either <see cref="ConversationId"/> or <see cref="MessageStore "/> may be set, but not both.
    /// If <see cref="ConversationId"/> is not null, and <see cref="MessageStore "/> is set, <see cref="ConversationId"/>
    /// will be reverted to null, and vice versa.
    /// </para>
    /// <para>
    /// This property may be null in the following cases:
    /// <list type="bullet">
    /// <item><description>The thread stores messages in the agent service and just has an id to the remove thread, instead of in an <see cref="ChatMessageStore"/>.</description></item>
    /// <item><description>This thread object is new it is not yet clear whether it will be backed by a server managed thread or an <see cref="ChatMessageStore"/>.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public ChatMessageStore? MessageStore
    {
        get => this._messageStore;
        internal set
        {
            if (this._messageStore is null && value is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(this._conversationId))
            {
                // If we have a conversation id already, we shouldn't switch the thread to use a message store
                // since it means that the thread will not work with the original agent anymore.
                throw new InvalidOperationException("Only the ConversationId or MessageStore may be set, but not both and switching from one to another is not supported.");
            }

            this._messageStore = Throw.IfNull(value);
        }
    }

    /// <summary>
    /// Gets or sets the <see cref="AIContextProvider"/> used by this thread to provide additional context to the AI model before each invocation.
    /// </summary>
    public AIContextProvider? AIContextProvider { get; internal set; }

    /// <inheritdoc/>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        JsonElement? storeState = this._messageStore?.Serialize(jsonSerializerOptions);

        JsonElement? aiContextProviderState = this.AIContextProvider?.Serialize(jsonSerializerOptions);

        var state = new ThreadState
        {
            ConversationId = this.ConversationId,
            StoreState = storeState is { ValueKind: not JsonValueKind.Undefined } ? storeState : null,
            AIContextProviderState = aiContextProviderState is { ValueKind: not JsonValueKind.Undefined } ? aiContextProviderState : null,
        };

        return JsonSerializer.SerializeToElement(state, AgentJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ThreadState)));
    }

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(AgentThreadMetadata)
            ? new AgentThreadMetadata(this.ConversationId)
            : base.GetService(serviceType, serviceKey)
            ?? this.AIContextProvider?.GetService(serviceType, serviceKey)
            ?? this.MessageStore?.GetService(serviceType, serviceKey);

    /// <inheritdoc />
    protected override async Task MessagesReceivedAsync(IEnumerable<ChatMessage> newMessages, CancellationToken cancellationToken = default)
    {
        switch (this)
        {
            case { ConversationId: not null }:
                // If the thread messages are stored in the service
                // there is nothing to do here, since invoking the
                // service should already update the thread.
                break;

            case { MessageStore: null }:
                // If there is no conversation id, and no store we can createa a default in memory store and add messages to it.
                this._messageStore = new InMemoryChatMessageStore();
                await this._messageStore!.AddMessagesAsync(newMessages, cancellationToken).ConfigureAwait(false);
                break;

            case { MessageStore: not null }:
                // If a store has been provided, we need to add the messages to the store.
                await this._messageStore!.AddMessagesAsync(newMessages, cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new UnreachableException();
        }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay =>
        this._conversationId is { } conversationId ? $"ConversationId = {conversationId}" :
        this._messageStore is InMemoryChatMessageStore inMemoryStore ? $"Count = {inMemoryStore.Count}" :
        this._messageStore is { } store ? $"Store = {store.GetType().Name}" :
        "Count = 0";

    internal sealed class ThreadState
    {
        public string? ConversationId { get; set; }

        public JsonElement? StoreState { get; set; }

        public JsonElement? AIContextProviderState { get; set; }
    }
}
