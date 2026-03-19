// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI;

/// <summary>
/// Provides specialized run options for <see cref="ChatClientAgent"/> instances, extending the base agent run options with chat-specific configuration.
/// </summary>
/// <remarks>
/// This class extends <see cref="AgentRunOptions"/> to provide additional configuration options that are specific to
/// chat client agents, in particular <see cref="ChatOptions"/>.
/// </remarks>
public sealed class ChatClientAgentRunOptions : AgentRunOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatClientAgentRunOptions"/> class.
    /// </summary>
    /// <param name="chatOptions">
    /// Optional chat options to customize the behavior of the chat client during this specific agent invocation.
    /// These options will be merged with the default chat options configured for the agent.
    /// </param>
    public ChatClientAgentRunOptions(ChatOptions? chatOptions = null)
    {
        this.ChatOptions = chatOptions;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatClientAgentRunOptions"/> class by copying values from the specified options.
    /// </summary>
    /// <param name="options">The options instance from which to copy values.</param>
    private ChatClientAgentRunOptions(ChatClientAgentRunOptions options)
        : base(options)
    {
        this.ChatOptions = options.ChatOptions?.Clone();
        this.ChatClientFactory = options.ChatClientFactory;
        this.StoreFinalFunctionResultContent = options.StoreFinalFunctionResultContent;
    }

    /// <summary>
    /// Gets or sets the chat options to apply to the agent invocation.
    /// </summary>
    /// <value>
    /// Chat options that control various aspects of the chat client's behavior, such as temperature, max tokens,
    /// tools, instructions, and other model-specific parameters. If <see langword="null"/>, the agent's default
    /// chat options will be used.
    /// </value>
    /// <remarks>
    /// These options are specific to this invocation and will be combined with the agent's default chat options.
    /// If both the agent and this run options specify the same option, the run options value typically takes precedence.
    /// In the case of collections, like <see cref="ChatOptions.Tools"/>, the collections will be unioned.
    /// </remarks>
    public ChatOptions? ChatOptions { get; set; }

    /// <summary>
    /// Gets or sets a factory function that can replace (typically via decorators) the chat client on a per-request basis.
    /// </summary>
    /// <value>
    /// A function that receives the agent's configured chat client and returns a potentially modified or entirely
    /// different chat client to use for this specific invocation. If <see langword="null"/>, the agent's default
    /// chat client will be used without modification.
    /// </value>
    public Func<IChatClient, IChatClient>? ChatClientFactory { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to store <see cref="FunctionResultContent"/> in chat history, if it was
    /// the last content returned from the <see cref="IChatClient"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This setting applies when the last content returned from the <see cref="IChatClient"/> is of type <see cref="FunctionResultContent"/>
    /// rather than for example <see cref="TextContent"/>.
    /// </para>
    /// <para>
    /// <see cref="FunctionResultContent"/> is typically only returned as the last content, if the function tool calling
    /// loop was terminated. In other cases, the <see cref="FunctionResultContent"/> would have been passed to the
    /// underlying service again as part of the next request, and new content with an answer to the user ask, for example <see cref="TextContent"/>,
    /// or new <see cref="FunctionCallContent"/> would have been produced.
    /// </para>
    /// <para>
    /// This option is only relevant if the agent does not use chat history storage in the underlying AI service. If
    /// chat history is not stored via a <see cref="ChatHistoryProvider"/>, the setting will have no effect. For agents
    /// that store chat history in the underlying AI service, final <see cref="FunctionResultContent"/> is never stored.
    /// </para>
    /// <para>
    /// When set to <see langword="false"/>, the behavior of chat history storage via <see cref="ChatHistoryProvider"/>
    /// matches the behavior of agents that store chat history in the underlying AI service. Note that this means that
    /// since the last stored content would have typically been <see cref="FunctionCallContent"/>, <see cref="FunctionResultContent"/>
    /// would need to be provided manually for the existing <see cref="FunctionCallContent"/> to continue the session.
    /// </para>
    /// <para>
    /// When set to <see langword="true"/>, the behavior of chat history storage via <see cref="ChatHistoryProvider"/>
    /// differs from the behavior of agents that store chat history in the underlying AI service.
    /// However, this does mean that a run could potentially be restarted without manually adding <see cref="FunctionResultContent"/>,
    /// since the <see cref="FunctionResultContent"/> would also be persisted in the chat history.
    /// Note however that if multiple function calls needed to be made, and termination happened before all functions were called,
    /// not all <see cref="FunctionCallContent"/> may have a corresponding <see cref="FunctionResultContent"/>, resulting in incomplete
    /// chat history regardless of this setting's value.
    /// </para>
    /// </remarks>
    /// <value>
    /// Defaults to <see langword="false"/>.
    /// </value>
    [Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
    public bool? StoreFinalFunctionResultContent { get; set; }

    /// <inheritdoc/>
    public override AgentRunOptions Clone() => new ChatClientAgentRunOptions(this);
}
