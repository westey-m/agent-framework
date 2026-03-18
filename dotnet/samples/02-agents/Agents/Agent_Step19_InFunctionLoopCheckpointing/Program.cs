// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates how the PersistChatHistoryAfterEachServiceCall option causes
// chat history to be persisted after each individual call to the AI service, rather than
// only at the end of the full agent run. When an agent uses tools, FunctionInvokingChatClient
// loops multiple times (service call → tool execution → service call), and by default the
// chat history is only persisted once the entire loop finishes. With this option enabled,
// intermediate messages (tool calls and results) are persisted after each service call,
// allowing you to inspect or recover them even if the process is interrupted mid-loop.
//
// The sample runs two multi-turn conversations: one using non-streaming (RunAsync) and one
// using streaming (RunStreamingAsync), to demonstrate correct behavior in both modes.

using System.ComponentModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential (e.g., ManagedIdentityCredential) to avoid
// latency issues, unintended credential probing, and potential security risks from fallback mechanisms.
AzureOpenAIClient openAIClient = new(new Uri(endpoint), new DefaultAzureCredential());
IChatClient chatClient = openAIClient.GetChatClient(deploymentName).AsIChatClient();

// Define multiple tools so the model makes several tool calls in a single run.
[Description("Get the current weather for a city.")]
static string GetWeather([Description("The city name.")] string city) =>
    city.ToUpperInvariant() switch
    {
        "SEATTLE" => "Seattle: 55°F, cloudy with light rain.",
        "NEW YORK" => "New York: 72°F, sunny and warm.",
        "LONDON" => "London: 48°F, overcast with fog.",
        "DUBLIN" => "Dublin: 43°F, overcast with fog.",
        _ => $"{city}: weather data not available."
    };

[Description("Get the current time in a city.")]
static string GetTime([Description("The city name.")] string city) =>
    city.ToUpperInvariant() switch
    {
        "SEATTLE" => "Seattle: 9:00 AM PST",
        "NEW YORK" => "New York: 12:00 PM EST",
        "LONDON" => "London: 5:00 PM GMT",
        "DUBLIN" => "Dublin: 5:00 PM GMT",
        _ => $"{city}: time data not available."
    };

// Create the agent with PersistChatHistoryAfterEachServiceCall enabled.
// The in-memory ChatHistoryProvider is used by default when no explicit provider is set,
// so we can inspect the chat history via session.TryGetInMemoryChatHistory().
AIAgent agent = chatClient.AsAIAgent(
    new ChatClientAgentOptions
    {
        Name = "WeatherAssistant",
        ChatOptions = new()
        {
            Instructions = "You are a helpful assistant. When asked about multiple cities, call the appropriate tool for each city.",
            Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(GetTime)]
        },
        PersistChatHistoryAfterEachServiceCall = true,
    });

await RunNonStreamingAsync();
await RunStreamingAsync();

async Task RunNonStreamingAsync()
{
    int lastChatHistorySize = 0;

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n=== Non-Streaming Mode ===");
    Console.ResetColor();

    AgentSession session = await agent.CreateSessionAsync();

    // First turn — ask about multiple cities so the model calls tools.
    const string Prompt = "What's the weather and time in Seattle, New York, and London?";
    PrintUserMessage(Prompt);

    var response = await agent.RunAsync(Prompt, session);
    PrintAgentResponse(response.Text);
    PrintChatHistory(session, "After run", ref lastChatHistorySize);

    // Second turn — follow-up to verify chat history is correct.
    const string FollowUp1 = "And Dublin?";
    PrintUserMessage(FollowUp1);

    response = await agent.RunAsync(FollowUp1, session);
    PrintAgentResponse(response.Text);
    PrintChatHistory(session, "After second run", ref lastChatHistorySize);

    // Third turn — follow-up to verify chat history is correct.
    const string FollowUp2 = "Which city is the warmest?";
    PrintUserMessage(FollowUp2);

    response = await agent.RunAsync(FollowUp2, session);
    PrintAgentResponse(response.Text);
    PrintChatHistory(session, "After third run", ref lastChatHistorySize);
}

async Task RunStreamingAsync()
{
    int lastChatHistorySize = 0;

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n=== Streaming Mode ===");
    Console.ResetColor();

    AgentSession session = await agent.CreateSessionAsync();

    // First turn — ask about multiple cities so the model calls tools.
    const string Prompt = "What's the weather and time in Seattle, New York, and London?";
    PrintUserMessage(Prompt);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("\n[Agent] ");
    Console.ResetColor();

    await foreach (var update in agent.RunStreamingAsync(Prompt, session))
    {
        Console.Write(update);

        // During streaming we should be able to see updates to the chat history
        // before the full run completes, as each service call is made and persisted.
        PrintChatHistory(session, "During run", ref lastChatHistorySize);
    }

    Console.WriteLine();
    PrintChatHistory(session, "After run", ref lastChatHistorySize);

    // Second turn — follow-up to verify chat history is correct.
    const string FollowUp1 = "And Dublin?";
    PrintUserMessage(FollowUp1);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("\n[Agent] ");
    Console.ResetColor();

    await foreach (var update in agent.RunStreamingAsync(FollowUp1, session))
    {
        Console.Write(update);

        // During streaming we should be able to see updates to the chat history
        // before the full run completes, as each service call is made and persisted.
        PrintChatHistory(session, "During second run", ref lastChatHistorySize);
    }

    Console.WriteLine();
    PrintChatHistory(session, "After second run", ref lastChatHistorySize);

    // Third turn — follow-up to verify chat history is correct.
    const string FollowUp2 = "Which city is the warmest?";
    PrintUserMessage(FollowUp2);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("\n[Agent] ");
    Console.ResetColor();

    await foreach (var update in agent.RunStreamingAsync(FollowUp2, session))
    {
        Console.Write(update);

        // During streaming we should be able to see updates to the chat history
        // before the full run completes, as each service call is made and persisted.
        PrintChatHistory(session, "During third run", ref lastChatHistorySize);
    }

    Console.WriteLine();
    PrintChatHistory(session, "After third run", ref lastChatHistorySize);
}

void PrintUserMessage(string message)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("\n[User] ");
    Console.ResetColor();
    Console.WriteLine(message);
}

void PrintAgentResponse(string? text)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("\n[Agent] ");
    Console.ResetColor();
    Console.WriteLine(text);
}

// Helper to print the current chat history from the session.
void PrintChatHistory(AgentSession session, string label, ref int lastChatHistorySize)
{
    if (session.TryGetInMemoryChatHistory(out var history) && history.Count != lastChatHistorySize)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n  [{label} — Chat history: {history.Count} message(s)]");
        foreach (var msg in history)
        {
            var preview = msg.Text?.Length > 80 ? msg.Text[..80] + "…" : msg.Text;
            var contentTypes = string.Join(", ", msg.Contents.Select(c => c.GetType().Name));
            Console.WriteLine($"    {msg.Role,-12} | {(string.IsNullOrWhiteSpace(preview) ? $"[{contentTypes}]" : preview)}");
        }

        Console.ResetColor();

        lastChatHistorySize = history.Count;
    }
}
