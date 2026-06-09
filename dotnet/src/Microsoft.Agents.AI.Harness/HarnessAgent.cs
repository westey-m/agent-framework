// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.Agents.AI.Compaction;
#if NET
using Microsoft.Agents.AI.Tools.Shell;
#endif
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.DiagnosticIds;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI;

/// <summary>
/// A pre-configured <see cref="DelegatingAIAgent"/> that wraps a <see cref="ChatClientAgent"/> with
/// function invocation, per-service-call chat history persistence, optional in-loop compaction, and a rich set
/// of default context providers and agent decorators.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HarnessAgent"/> assembles the following pipeline from a caller-supplied <see cref="IChatClient"/>:
/// <list type="number">
/// <item><description><see cref="FunctionInvokingChatClient"/> — automatic function/tool invocation.</description></item>
/// <item><description><see cref="MessageInjectingChatClient"/> — allows external code to inject messages into the conversation mid-stream.</description></item>
/// <item><description><see cref="PerServiceCallChatHistoryPersistingChatClient"/> — persists chat history after every individual service call within a function-invocation loop.</description></item>
/// <item><description><see cref="AIContextProviderChatClient"/> with a <see cref="CompactionProvider"/> — applies context-window compaction before each call so long function-invocation loops do not overflow the context window (only when <see cref="HarnessAgentOptions.MaxContextWindowTokens"/> and <see cref="HarnessAgentOptions.MaxOutputTokens"/> are provided).</description></item>
/// </list>
/// </para>
/// <para>
/// By default, the following context providers are included (each can be disabled via <see cref="HarnessAgentOptions"/>):
/// <list type="bullet">
/// <item><description><see cref="TodoProvider"/> — todo list management.</description></item>
/// <item><description><see cref="AgentModeProvider"/> — agent mode tracking (plan/execute).</description></item>
/// <item><description><see cref="FileMemoryProvider"/> — file-based session memory.</description></item>
/// <item><description><see cref="FileAccessProvider"/> — shared file access.</description></item>
/// <item><description><see cref="AgentSkillsProvider"/> — skill discovery and loading.</description></item>
/// </list>
/// </para>
/// <para>
/// The agent is also wrapped with the following decorators by default (each can be disabled):
/// <list type="bullet">
/// <item><description><see cref="ToolApprovalAgent"/> — "don't ask again" tool approval rules.</description></item>
/// <item><description><see cref="OpenTelemetryAgent"/> — OpenTelemetry instrumentation.</description></item>
/// </list>
/// </para>
/// <para>
/// A <see cref="HostedWebSearchTool"/> is added to the chat options by default (can be disabled via
/// <see cref="HarnessAgentOptions.DisableWebSearch"/>).
/// </para>
/// <para>
/// The underlying <see cref="ChatClientAgent"/> is configured with
/// <see cref="ChatClientAgentOptions.UseProvidedChatClientAsIs"/> and
/// <see cref="ChatClientAgentOptions.RequirePerServiceCallChatHistoryPersistence"/> set to <see langword="true"/>
/// to match the manually-assembled pipeline.
/// </para>
/// <para>
/// When no <see cref="HarnessAgentOptions.ChatHistoryProvider"/> is supplied and compaction is enabled, the agent
/// defaults to an <see cref="InMemoryChatHistoryProvider"/> whose chat reducer applies the same compaction strategy,
/// keeping in-memory history from growing unboundedly across sessions. When compaction is disabled, the default
/// <see cref="InMemoryChatHistoryProvider"/> is used without a chat reducer.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public sealed class HarnessAgent : DelegatingAIAgent
{
    /// <summary>
    /// The built-in default system instructions used when <see cref="ChatOptions.Instructions"/> is not set.
    /// </summary>
    public const string DefaultInstructions =
        """
        You are a helpful AI assistant that uses tools to complete tasks.

        ## General guidelines

        - Think through the task before acting. Break complex work into clear steps.
        - Use the tools available to you to gather information, perform actions, and verify results.
        - Explain your reasoning and thought process as you work through tasks.
        - Explain what you learned and what you are going to do next between tool calls, so the user can follow along with your thought process.
        - Avoid making more than 4 tool calls in a row without explaining what you are doing.
        - If a tool call fails or returns unexpected results, adapt your approach rather than repeating the same call.
        - When you have completed the task, present a clear and concise summary of what you did and what you found.
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessAgent"/> class.
    /// </summary>
    /// <param name="chatClient">
    /// The <see cref="IChatClient"/> that provides access to the underlying AI model.
    /// The agent wraps this client in a function-invocation, per-service-call persistence,
    /// and compaction pipeline automatically.
    /// </param>
    /// <param name="options">
    /// Optional configuration options for the agent, including instructions override, tools,
    /// additional context providers, chat history provider, and compaction settings.
    /// When <see langword="null"/>, the agent uses built-in default settings with compaction disabled.
    /// </param>
    /// <param name="loggerFactory">
    /// Optional logger factory for creating loggers used by the agent and its components.
    /// </param>
    /// <param name="services">
    /// Optional service provider for resolving dependencies required by AI functions and other agent components.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="chatClient"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="HarnessAgentOptions.MaxContextWindowTokens"/> is not positive, or
    /// <see cref="HarnessAgentOptions.MaxOutputTokens"/> is negative or greater than or equal to
    /// <see cref="HarnessAgentOptions.MaxContextWindowTokens"/> (when both are provided).
    /// </exception>
    public HarnessAgent(IChatClient chatClient, HarnessAgentOptions? options = null, ILoggerFactory? loggerFactory = null, IServiceProvider? services = null)
        : base(BuildAgent(
            Throw.IfNull(chatClient),
            options,
            loggerFactory,
            services))
    {
    }

    private static AIAgent BuildAgent(IChatClient chatClient, HarnessAgentOptions? options, ILoggerFactory? loggerFactory, IServiceProvider? services)
    {
        ChatClientAgent innerAgent = BuildInnerAgent(chatClient, options, loggerFactory, services);

        AIAgentBuilder builder = innerAgent.AsBuilder();

        if (options?.DisableToolApproval is not true)
        {
            builder.UseToolApproval(options?.ToolApprovalAgentOptions);
        }

        if (options?.DisableOpenTelemetry is not true)
        {
            builder.UseOpenTelemetry(sourceName: options?.OpenTelemetrySourceName);
        }

        return builder.Build(services);
    }

    private static ChatClientAgent BuildInnerAgent(IChatClient chatClient, HarnessAgentOptions? options, ILoggerFactory? loggerFactory, IServiceProvider? services)
    {
        int? maxContextWindowTokens = options?.MaxContextWindowTokens;
        int? maxOutputTokens = options?.MaxOutputTokens;
        bool compactionEnabled = maxContextWindowTokens.HasValue && maxOutputTokens.HasValue;

        ContextWindowCompactionStrategy? compactionStrategy = compactionEnabled
            ? new ContextWindowCompactionStrategy(
                maxContextWindowTokens: maxContextWindowTokens!.Value,
                maxOutputTokens: maxOutputTokens!.Value)
            : null;

        ChatHistoryProvider chatHistoryProvider = options?.ChatHistoryProvider
            ?? (compactionStrategy is not null
                ? new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
                {
                    ChatReducer = compactionStrategy.AsChatReducer(),
                })
                : new InMemoryChatHistoryProvider());

        string harnessInstructions = options?.HarnessInstructions ?? DefaultInstructions;
        string? agentInstructions = options?.ChatOptions?.Instructions;

        string instructions = (string.IsNullOrWhiteSpace(harnessInstructions), string.IsNullOrWhiteSpace(agentInstructions)) switch
        {
            (true, true) => harnessInstructions,
            (true, false) => agentInstructions!,
            (false, true) => harnessInstructions,
            (false, false) => $"{harnessInstructions}\n\n{agentInstructions}",
        };

        ChatOptions chatOptions = BuildChatOptions(options, instructions, maxOutputTokens);

        CompactionProvider? compactionProvider = compactionStrategy is not null
            ? new CompactionProvider(compactionStrategy, loggerFactory: loggerFactory)
            : null;

        IEnumerable<AIContextProvider> contextProviders = BuildContextProviders(options, loggerFactory);

        ChatClientBuilder chatClientBuilder = chatClient.AsBuilder();

        if (options?.DisableNonApprovalRequiredFunctionBypassing is not true)
        {
            chatClientBuilder.UseNonApprovalRequiredFunctionBypassing();
        }

        ChatClientBuilder pipeline = chatClientBuilder
            .UseFunctionInvocation(loggerFactory, configure: options?.MaximumIterationsPerRequest is int maxIterations
                ? ficc => ficc.MaximumIterationsPerRequest = maxIterations
                : null)
            .UseMessageInjection()
            .UsePerServiceCallChatHistoryPersistence();

        if (compactionProvider is not null)
        {
            pipeline.UseAIContextProviders(compactionProvider);
        }

        return pipeline
            .BuildAIAgent(new ChatClientAgentOptions
            {
                Id = options?.Id,
                Name = options?.Name,
                Description = options?.Description,
                ChatOptions = chatOptions,
                ChatHistoryProvider = chatHistoryProvider,
                AIContextProviders = contextProviders,
                UseProvidedChatClientAsIs = true,
                RequirePerServiceCallChatHistoryPersistence = true,
                WarnOnChatHistoryProviderConflict = false,
                ThrowOnChatHistoryProviderConflict = false,
            },
            loggerFactory,
            services);
    }

    private static ChatOptions BuildChatOptions(HarnessAgentOptions? options, string instructions, int? maxOutputTokens)
    {
        ChatOptions result = options?.ChatOptions?.Clone() ?? new ChatOptions();
        result.Instructions = instructions;

        if (maxOutputTokens.HasValue)
        {
            result.MaxOutputTokens ??= maxOutputTokens.Value;
        }

        if (options?.DisableWebSearch is not true)
        {
            result.Tools ??= [];
            result.Tools.Add(new HostedWebSearchTool());
        }

#if NET
        if (options?.ShellExecutor is ShellExecutor shellExecutor)
        {
            result.Tools ??= [];
            result.Tools.Add(shellExecutor.AsAIFunction());
        }
#endif

        return result;
    }

    private static List<AIContextProvider> BuildContextProviders(HarnessAgentOptions? options, ILoggerFactory? loggerFactory)
    {
        var providers = new List<AIContextProvider>();

        if (options?.DisableTodoProvider is not true)
        {
            providers.Add(new TodoProvider());
        }

        if (options?.DisableAgentModeProvider is not true)
        {
            providers.Add(new AgentModeProvider(options?.AgentModeProviderOptions));
        }

        if (options?.DisableFileMemory is not true)
        {
            AgentFileStore fileMemoryStore = options?.FileMemoryStore
                ?? new FileSystemAgentFileStore(
                    Path.Combine(Directory.GetCurrentDirectory(), "agent-file-memory"));

            providers.Add(new FileMemoryProvider(
                fileMemoryStore,
                _ => new FileMemoryState
                {
                    WorkingFolder = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString(),
                }));
        }

        if (options?.DisableFileAccess is not true)
        {
            AgentFileStore fileAccessStore = options?.FileAccessStore
                ?? new FileSystemAgentFileStore(
                    Path.Combine(Directory.GetCurrentDirectory(), "working"));

            providers.Add(new FileAccessProvider(fileAccessStore));
        }

        if (options?.DisableAgentSkillsProvider is not true)
        {
            AgentSkillsProvider skillsProvider = options?.AgentSkillsSource is AgentSkillsSource source
                ? new AgentSkillsProvider(source, loggerFactory: loggerFactory)
                : new AgentSkillsProvider(Directory.GetCurrentDirectory(), loggerFactory: loggerFactory);

            providers.Add(skillsProvider);
        }

        if (options?.BackgroundAgents is IEnumerable<AIAgent> backgroundAgents)
        {
            var materializedAgents = backgroundAgents.ToList();
            if (materializedAgents.Count > 0)
            {
                providers.Add(new BackgroundAgentsProvider(materializedAgents, options.BackgroundAgentsProviderOptions));
            }
        }

#if NET
        if (options?.ShellExecutor is ShellExecutor shellExecutor)
        {
            providers.Add(new ShellEnvironmentProvider(shellExecutor, options.ShellEnvironmentProviderOptions));
        }
#endif

        if (options?.AIContextProviders is IEnumerable<AIContextProvider> userProviders)
        {
            providers.AddRange(userProviders);
        }

        return providers;
    }
}
