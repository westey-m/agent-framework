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

const int MaxContextWindowTokens = 1_050_000;
const int MaxOutputTokens = 128_000;

// Create a compaction strategy based on the model's context window.
// gpt-5.4: 1,050,000 token context window, 128,000 max output tokens.
// Defaults: tool result eviction at 50% of input budget, truncation at 80%.
var compactionStrategy = new ContextWindowCompactionStrategy(
    maxContextWindowTokens: MaxContextWindowTokens,
    maxOutputTokens: MaxOutputTokens);

// Create an OpenAIClient that communicates with the Foundry responses service and get an IChatClient with stored output disabled
// so that chat history is managed locally by the agent framework.
// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
OpenAIClientOptions clientOptions = new() { Endpoint = new Uri(endpoint), RetryPolicy = new ClientRetryPolicy(3) };
IChatClient chatClient = new OpenAIClient(new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"), clientOptions)
    .GetResponsesClient()
    .AsIChatClientWithStoredOutputDisabled(deploymentName)
    .AsBuilder()
    .UseFunctionInvocation()
    .UsePerServiceCallChatHistoryPersistence()
    .UseAIContextProviders(new CompactionProvider(compactionStrategy))
    .Build();

// Create web browsing tools for downloading and converting HTML pages to markdown.
var webBrowsingTools = new WebBrowsingTools();

// Create a ChatClientAgent with the Harness providers (TodoProvider and AgentModeProvider)
// and research-focused instructions including the mandatory planning workflow.
var instructions =
    """
    You are a research assistant. When given a research topic, research it thoroughly using web search and web browsing. Use your knowledge to form good search queries and hypotheses, but always verify claims with the tools available to you rather than relying on memory alone.

    **Mandatory planning workflow**

    For every new substantive user request, including short factual questions, you must begin in plan mode and follow this sequence:

    1. Analyze the request.
    2. Ask for clarifications where needed.
      1. When asking for clarification and you have specific options in mind, present them to the user with numbers, so they can respond with the number instead of having to retype the entire response.
      2. Always also allow the user to respond with free-form text in case they want to provide information or context that you didn't specifically ask for.
    3. Create one or more todo items.
    4. Write the plan to a memory file, so that it is retained even if compaction happens. Make sure to update the plan file if the user requests changes.
    5. Present the plan to the user.
    6. Ask for approval to switch to execute mode and process the plan.
    7. When approval is granted, always switch to execute mode, execute the plan and complete the todos.
    8. In execute mode, work autonomously — use your best judgement to make decisions and keep progressing without asking the user questions. The goal is to have a complete, useful result ready when the user returns.
    9. If you encounter ambiguity or an unexpected situation during execution, choose the most reasonable option, note your choice, and keep going.
    10. Continue working, thinking and calling tools until you have the research result for the user.

    Explain your reasoning and thought process as you work through the tasks.
    Explain what you learned and what you are going to do next between tool calls, so the user can follow along with your thought process.
    When calling many tools in a row, provide an explanation to the user after each 4 tool calls (or fewer) to help the user understand what you're doing and why.
    Do not answer the underlying question before the plan has been presented and approved.
    This rule applies even when the answer seems obvious or the task seems small.
    For short requests, use a brief micro-plan rather than skipping planning.

    The only exceptions are:
    - greetings,
    - pure acknowledgments,
    - clarification questions needed to form the plan,
    - follow-up questions about results you have already presented,
    - meta-discussion about the workflow itself.

    When the task is complete, switch back to plan mode for the next request, even if the next request is just a short question.

    **Todo management**

    Mark each todo complete as you finish it so the list stays current.
    If a todo turns out to be unnecessary or is blocked, remove it and briefly explain why.

    **Research quality**

    Consult multiple sources when possible and cross-reference key claims.
    When sources disagree, note the discrepancy and explain which source you consider more reliable and why.
    If a web page fails to load or a search returns irrelevant results, try alternative search queries or sources before moving on.
    Track your sources — you will need them when presenting results.

    **Presenting results**

    When presenting your final findings:
    - Use clear sections with headings for each major topic or sub-question.
    - Cite your sources inline (e.g., "According to [source name](URL), ...").
    - End with a brief summary of key takeaways.
    - Save the final research report to file memory so it survives compaction and can be referenced later.

    **File memory**

    When you download web pages or receive large amounts of data, save them to file memory using the FileMemory_SaveFile tool.
    This ensures the data remains accessible even if older context is compacted or truncated during long research sessions.
    Use descriptive file names (e.g., "openai_pricing_page.md") and include a brief description for large files.
    Also save intermediate notes and findings as you go — this helps with long multi-step research where early findings inform later steps.
    Before starting new research, check file memory with FileMemory_ListFiles and FileMemory_SearchFiles for relevant prior downloads.
    When a temporary file is no longer needed, delete it to keep file memory tidy.
    """;

AIAgent agent = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        Name = "ResearchAgent",
        Description = "A research assistant that plans and executes research tasks.",
        AIContextProviders =
        [
            new TodoProvider(),
            new AgentModeProvider(),
            new FileMemoryProvider(
                new FileSystemAgentFileStore(Path.Combine(AppContext.BaseDirectory, "agent-files")),
                (_) => new FileMemoryState() { WorkingFolder = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString() })
        ],
        RequirePerServiceCallChatHistoryPersistence = true,
        UseProvidedChatClientAsIs = true,
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
            Reasoning = new() { Effort = ReasoningEffort.Medium },
            Tools = [ResponseTool.CreateWebSearchTool().AsAITool(), .. webBrowsingTools.Tools],
        },
    });

// Run the interactive console session using the shared HarnessConsole helper.
await HarnessConsole.RunAgentAsync(agent, title: "Research Assistant", userPrompt: "Enter a research topic to get started.", maxContextWindowTokens: MaxContextWindowTokens, maxOutputTokens: MaxOutputTokens);
