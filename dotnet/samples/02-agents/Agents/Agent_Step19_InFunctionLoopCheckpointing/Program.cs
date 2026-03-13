// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates how the PersistChatHistoryAfterEachServiceCall option causes
// chat history to be persisted after each individual call to the AI service, rather than
// only at the end of the full agent run. When an agent uses tools, FunctionInvokingChatClient
// loops multiple times (service call → tool execution → service call), and by default the
// chat history is only persisted once the entire loop finishes. With this option enabled,
// intermediate messages (tool calls and results) are persisted after each service call,
// allowing you to inspect or recover them even if the process is interrupted mid-loop.
//
// The sample uses RunStreamingAsync so that we can observe the chat history growing
// after each service call within a single agent run.

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
        _ => $"{city}: weather data not available."
    };

[Description("Get the current time in a city.")]
static string GetTime([Description("The city name.")] string city) =>
    city.ToUpperInvariant() switch
    {
        "SEATTLE" => "Seattle: 9:00 AM PST",
        "NEW YORK" => "New York: 12:00 PM EST",
        "LONDON" => "London: 5:00 PM GMT",
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

AgentSession session = await agent.CreateSessionAsync();

// Ask about multiple cities — the model will need to call tools for each city,
// resulting in multiple service calls within a single agent run.
string prompt = "What's the weather and time in Seattle, New York, and London?";

Console.ForegroundColor = ConsoleColor.Cyan;
Console.Write("\n[User] ");
Console.ResetColor();
Console.WriteLine(prompt);

PrintChatHistory("Before run");

Console.ForegroundColor = ConsoleColor.Cyan;
Console.Write("\n[Agent] ");
Console.ResetColor();

// Use RunStreamingAsync to observe the response as it streams.
await foreach (var update in agent.RunStreamingAsync(prompt, session))
{
    Console.Write(update);
}

Console.WriteLine();

PrintChatHistory("After run");

// Run a second turn to show that chat history accumulated correctly.
string followUp = "Which city is the warmest?";
Console.ForegroundColor = ConsoleColor.Cyan;
Console.Write("\n[User] ");
Console.ResetColor();
Console.WriteLine(followUp);

Console.ForegroundColor = ConsoleColor.Cyan;
Console.Write("\n[Agent] ");
Console.ResetColor();

await foreach (var update in agent.RunStreamingAsync(followUp, session))
{
    Console.Write(update);
}

Console.WriteLine();

PrintChatHistory("After second run");

// Helper to print the current chat history from the session.
void PrintChatHistory(string label)
{
    if (session.TryGetInMemoryChatHistory(out var history))
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n  [{label} — Chat history: {history.Count} message(s)]");
        foreach (var msg in history)
        {
            var preview = msg.Text?.Length > 80 ? msg.Text[..80] + "…" : msg.Text;
            var contentTypes = string.Join(", ", msg.Contents.Select(c => c.GetType().Name));
            Console.WriteLine($"    {msg.Role,-12} | {preview ?? $"[{contentTypes}]"}");
        }

        Console.ResetColor();
    }
}
