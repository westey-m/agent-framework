// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates how to use a ChatClientAgent with the Harness AIContextProviders
// (TodoProvider and AgentModeProvider) for interactive research tasks with web search
// capabilities powered by Azure AI Foundry.
// The agent plans research tasks, creates a todo list, gets user approval,
// and then executes each step — all within an interactive conversation loop.
//
// Special commands:
//   /todos  — Display the current todo list without invoking the agent.
//   exit    — End the session.

#pragma warning disable OPENAI001 // Suppress experimental API warnings for Responses API usage.
#pragma warning disable MAAI001  // Suppress experimental API warnings for Agents AI experiments.

using System.ClientModel.Primitives;
using Azure.Identity;
using Harness.Shared.Console;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Responses;
using SampleApp;

var endpoint = Environment.GetEnvironmentVariable("AZURE_FOUNDRY_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_FOUNDRY_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME") ?? "gpt-5.4";

// Create a compaction strategy based on the model's context window.
// gpt-5.4: 1,050,000 token context window, 128,000 max output tokens.
// Defaults: tool result eviction at 50% of input budget, truncation at 80%.
var compactionStrategy = new ContextWindowCompactionStrategy(
    maxContextWindowTokens: 1_050_000,
    maxOutputTokens: 128_000);

// Create an OpenAIClient that communicates with the Foundry responses service and get an IChatClient with stored output disabled
// so that chat history is managed locally by the agent framework.
// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
OpenAIClientOptions clientOptions = new() { Endpoint = new Uri(endpoint) };
IChatClient chatClient = new OpenAIClient(new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"), clientOptions)
    .GetResponsesClient()
    .AsIChatClientWithStoredOutputDisabled(deploymentName)
    .AsBuilder()
    .UseAIContextProviders(new CompactionProvider(compactionStrategy))
    .Build();

// Create web browsing tools for downloading and converting HTML pages to markdown.
var webBrowsingTools = new WebBrowsingTools();

// Create a ChatClientAgent with the Harness providers (TodoProvider and AgentModeProvider)
// and research-focused instructions including the mandatory planning workflow.
var instructions =
    """
    You are a research assistant. When given a research topic, research it thoroughly using web search and web browsing. Don't rely on your own knowledge — use the tools available to you to find up-to-date information.

    **Mandatory planning workflow**

    For every new substantive user request, including short factual questions, you must begin in plan mode and follow this sequence:

    1. Analyze the request.
    2. Ask for clarifications where needed.
      1. When asking for clarification and you have specific options in mind, present them to the user with numbers, so they can respond with the number instead of having to retype the entire response.
      2. Always also allow the user to respond with free-form text in case they want to provide information or context that you didn't specifically ask for.
    3. Create one or more todo items.
    4. Present the plan to the user.
    5. Ask for approval to switch to execute mode and process the plan.
    6. When approval is granted, always switch to execute mode, execute the plan and complete the todos.

    Explain your reasoning and thought process as you work through the tasks.
    Explain what you learned and what you are going to do next between tool calls, so the user can follow along with your thought process.
    Don't call many tools in a row without providing some explanation in between to help the user understand what you're doing and why.
    Do not answer the underlying question before the plan has been presented and approved.
    This rule applies even when the answer seems obvious or the task seems small.
    For short requests, use a brief micro-plan rather than skipping planning.

    The only exceptions are:
    - greetings,
    - pure acknowledgments,
    - clarification questions needed to form the plan,
    - meta-discussion about the workflow itself.

    When the task is complete, switch back to plan mode for the next request, even if the next request is just a short question.
    """;

AIAgent agent = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        Name = "ResearchAgent",
        Description = "A research assistant that plans and executes research tasks.",
        AIContextProviders = [new TodoProvider(), new AgentModeProvider()],
        ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
        {
            ChatReducer = compactionStrategy.AsChatReducer(),
        }),
        ChatOptions = new ChatOptions
        {
            // Set a high token limit for long research tasks with many tool calls and long outputs.
            // This matches gpt-5.4's max output tokens, and should be adjusted depending on the model used and expected response length.
            MaxOutputTokens = 128_000,
            Instructions = instructions,
            Reasoning = new() { Effort = ReasoningEffort.High },
            Tools = [ResponseTool.CreateWebSearchTool().AsAITool(), .. webBrowsingTools.Tools],
        },
    });

// Run the interactive console session using the shared HarnessConsole helper.
await HarnessConsole.RunAgentAsync(agent, title: "Research Assistant", userPrompt: "Enter a research topic to get started.", maxContextWindowTokens: 1_050_000, maxOutputTokens: 128_000);
