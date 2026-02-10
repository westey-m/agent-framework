// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI;

/// <summary>
/// Represents configuration options for <see cref="InMemoryChatHistoryProvider"/>.
/// </summary>
public sealed class InMemoryChatHistoryProviderOptions
{
    /// <summary>
    /// Gets or sets an optional delegate that initializes the provider state on the first invocation.
    /// If <see langword="null"/>, a default initializer that creates an empty state will be used.
    /// </summary>
    public Func<AgentSession?, InMemoryChatHistoryProvider.State>? StateInitializer { get; set; }

    /// <summary>
    /// Gets or sets an optional <see cref="IChatReducer"/> instance used to process, reduce, or optimize chat messages.
    /// This can be used to implement strategies like message summarization, truncation, or cleanup.
    /// </summary>
    public IChatReducer? ChatReducer { get; set; }

    /// <summary>
    /// Gets or sets when the message reducer should be invoked.
    /// The default is <see cref="ChatReducerTriggerEvent.BeforeMessagesRetrieval"/>,
    /// which applies reduction logic when messages are retrieved for agent consumption.
    /// </summary>
    /// <remarks>
    /// Message reducers enable automatic management of message storage by implementing strategies to
    /// keep memory usage under control while preserving important conversation context.
    /// </remarks>
    public ChatReducerTriggerEvent ReducerTriggerEvent { get; set; } = ChatReducerTriggerEvent.BeforeMessagesRetrieval;

    /// <summary>
    /// Gets or sets an optional key to use for storing the state in the <see cref="AgentSession.StateBag"/>.
    /// If <see langword="null"/>, a default key will be used.
    /// </summary>
    public string? StateKey { get; set; }

    /// <summary>
    /// Gets or sets optional JSON serializer options for serializing the state of this provider.
    /// This is valuable for cases like when the chat history contains custom <see cref="AIContent"/> types
    /// and source generated serializers are required, or Native AOT / Trimming is required.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Defines the events that can trigger a reducer in the <see cref="InMemoryChatHistoryProvider"/>.
    /// </summary>
    public enum ChatReducerTriggerEvent
    {
        /// <summary>
        /// Trigger the reducer when a new message is added.
        /// <see cref="AIContextProvider.InvokedAsync"/> will only complete when reducer processing is done.
        /// </summary>
        AfterMessageAdded,

        /// <summary>
        /// Trigger the reducer before messages are retrieved from the provider.
        /// The reducer will process the messages before they are returned to the caller.
        /// </summary>
        BeforeMessagesRetrieval
    }
}
