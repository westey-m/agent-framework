// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Harness.Shared.Console;

/// <summary>
/// Provides a reusable interactive console loop for running an <see cref="AIAgent"/>
/// with streaming output, tool call display, spinner, and mode-aware prompts.
/// </summary>
public static class HarnessConsole
{
    /// <summary>
    /// Runs an interactive console session with the specified agent.
    /// Supports streaming output, tool call display, spinner animation,
    /// and the <c>/todos</c> command.
    /// </summary>
    /// <param name="agent">The agent to interact with.</param>
    /// <param name="title">The title displayed in the console header.</param>
    /// <param name="userPrompt">A short prompt to the user, displayed below the title.</param>
    /// <param name="maxContextWindowTokens">Optional max context window size in tokens. When set, usage is displayed as a percentage.</param>
    /// <param name="maxOutputTokens">Optional max output tokens. Used with <paramref name="maxContextWindowTokens"/> to show input/output budget breakdown.</param>
    public static async Task RunAgentAsync(AIAgent agent, string title, string userPrompt, int? maxContextWindowTokens = null, int? maxOutputTokens = null)
    {
        var todoProvider = agent.GetService<TodoProvider>();
        var modeProvider = agent.GetService<AgentModeProvider>();

        System.Console.WriteLine($"=== {title} ===");
        System.Console.WriteLine(userPrompt);
        System.Console.WriteLine("Commands: /todos (show todo list), /mode [plan|execute] (show or switch mode), exit (quit)");
        System.Console.WriteLine();

        AgentSession session = await agent.CreateSessionAsync();

        WritePrompt(modeProvider, session);
        string? userInput = System.Console.ReadLine();

        while (!string.IsNullOrWhiteSpace(userInput) && !userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            if (userInput.Equals("/todos", StringComparison.OrdinalIgnoreCase))
            {
                PrintTodos(todoProvider, session);
            }
            else if (userInput.StartsWith("/mode", StringComparison.OrdinalIgnoreCase))
            {
                HandleModeCommand(modeProvider, session, userInput);
            }
            else
            {
                await StreamAgentResponseAsync(agent, session, modeProvider, userInput, maxContextWindowTokens, maxOutputTokens);
            }

            WritePrompt(modeProvider, session);
            userInput = System.Console.ReadLine();
        }

        System.Console.ResetColor();
        System.Console.WriteLine("Goodbye!");
    }

    private static async Task StreamAgentResponseAsync(AIAgent agent, AgentSession session, AgentModeProvider? modeProvider, string userInput, int? maxContextWindowTokens, int? maxOutputTokens)
    {
        // Initial user input
        var approvalRequests = await StreamAndCollectApprovalsAsync(agent.RunStreamingAsync(userInput, session), modeProvider, session, maxContextWindowTokens, maxOutputTokens);
        var messagesToSend = PromptForApprovals(approvalRequests);

        // Loop while there are approval responses to send back
        while (messagesToSend is not null)
        {
            approvalRequests = await StreamAndCollectApprovalsAsync(agent.RunStreamingAsync(messagesToSend, session), modeProvider, session, maxContextWindowTokens, maxOutputTokens);
            messagesToSend = PromptForApprovals(approvalRequests);
        }
    }

    private static async Task<List<ToolApprovalRequestContent>> StreamAndCollectApprovalsAsync(IAsyncEnumerable<AgentResponseUpdate> updates, AgentModeProvider? modeProvider, AgentSession session, int? maxContextWindowTokens, int? maxOutputTokens)
    {
        var approvalRequests = new List<ToolApprovalRequestContent>();
        string mode = modeProvider?.GetMode(session) ?? "unknown";
        System.Console.ForegroundColor = GetModeColor(mode);
        System.Console.Write($"\n[{mode}] Agent: ");

        var spinner = new Spinner();
        spinner.Start();
        bool hasTextOutput = false;
        bool hasReceivedAnyText = false;

        try
        {
            await foreach (var update in updates)
            {
                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent functionCall)
                    {
                        await spinner.StopAsync();
                        System.Console.ForegroundColor = ConsoleColor.DarkYellow;
                        System.Console.Write(hasTextOutput ? "\n\n  🔧 Calling tool: " : "\n  🔧 Calling tool: ");
                        System.Console.Write($"{ToolCallFormatter.Format(functionCall)}...");
                        System.Console.ForegroundColor = GetModeColor(mode);
                        hasTextOutput = false;
                        spinner.Start();
                    }
                    else if (content is ToolCallContent toolCall)
                    {
                        await spinner.StopAsync();
                        System.Console.ForegroundColor = ConsoleColor.DarkYellow;
                        System.Console.Write(hasTextOutput ? "\n\n  🔧 Calling tool: " : "\n  🔧 Calling tool: ");
                        System.Console.Write($"{toolCall}...");
                        System.Console.ForegroundColor = GetModeColor(mode);
                        hasTextOutput = false;
                        spinner.Start();
                    }
                    else if (content is ToolApprovalRequestContent approvalRequest)
                    {
                        await spinner.StopAsync();
                        approvalRequests.Add(approvalRequest);
                        string toolName = approvalRequest.ToolCall is FunctionCallContent fc ? ToolCallFormatter.Format(fc) : approvalRequest.ToolCall?.ToString() ?? "unknown";
                        System.Console.ForegroundColor = ConsoleColor.Yellow;
                        System.Console.Write(hasTextOutput ? "\n\n  ⚠️ Approval needed: " : "\n  ⚠️ Approval needed: ");
                        System.Console.Write(toolName);
                        System.Console.ForegroundColor = GetModeColor(mode);
                        hasTextOutput = false;
                    }
                    else if (content is ErrorContent errorContent)
                    {
                        await spinner.StopAsync();
                        System.Console.ForegroundColor = ConsoleColor.Red;
                        System.Console.Write($"\n  ❌ Error: {errorContent.Message}");
                        if (errorContent.ErrorCode is not null)
                        {
                            System.Console.Write($" (code: {errorContent.ErrorCode})");
                        }

                        System.Console.ForegroundColor = GetModeColor(mode);
                    }
                    else if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                    {
                        await spinner.StopAsync();

                        if (!hasTextOutput)
                        {
                            System.Console.Write("\n");
                            hasTextOutput = true;
                            hasReceivedAnyText = true;
                        }

                        System.Console.ForegroundColor = ConsoleColor.DarkMagenta;
                        System.Console.Write(reasoning.Text);
                        System.Console.ForegroundColor = GetModeColor(mode);
                    }
                    else if (content is UsageContent usage)
                    {
                        await spinner.StopAsync();
                        System.Console.ForegroundColor = ConsoleColor.DarkGray;
                        System.Console.Write("\n\n  📊 Tokens");
                        if (usage.Details is not null)
                        {
                            WriteUsageBreakdown(usage.Details, maxContextWindowTokens, maxOutputTokens);
                        }
                        else
                        {
                            System.Console.Write(" —");
                        }
                        System.Console.ForegroundColor = GetModeColor(mode);
                        hasTextOutput = false;
                    }
                }

                if (string.IsNullOrEmpty(update.Text))
                {
                    continue;
                }

                await spinner.StopAsync();

                if (!hasTextOutput)
                {
                    System.Console.Write("\n");
                    hasTextOutput = true;
                    hasReceivedAnyText = true;
                }

                string currentMode = modeProvider?.GetMode(session) ?? "unknown";
                if (currentMode != mode)
                {
                    mode = currentMode;
                    System.Console.ForegroundColor = GetModeColor(mode);
                }

                System.Console.Write(update.Text);
            }
        }
        catch (Exception ex)
        {
            await spinner.StopAsync();
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.Write($"\n  ❌ Stream error: {ex.GetType().Name}:\n{ex}");
        }

        await spinner.StopAsync();

        if (!hasReceivedAnyText && approvalRequests.Count == 0)
        {
            System.Console.ForegroundColor = ConsoleColor.DarkYellow;
            System.Console.Write("\n  (no text response from agent)");
        }

        System.Console.ResetColor();
        System.Console.WriteLine();
        System.Console.WriteLine();

        return approvalRequests;
    }

    /// <summary>
    /// Prompts the user for approval of each tool approval request.
    /// Returns a list of messages to send back to the agent, or <see langword="null"/> if there are no requests.
    /// </summary>
    private static List<ChatMessage>? PromptForApprovals(List<ToolApprovalRequestContent> approvalRequests)
    {
        if (approvalRequests.Count == 0)
        {
            return null;
        }

        var responses = new List<AIContent>();
        foreach (var request in approvalRequests)
        {
            string toolName = request.ToolCall is FunctionCallContent fc ? ToolCallFormatter.Format(fc) : request.ToolCall?.ToString() ?? "unknown";

            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"\n  🔐 Tool approval required: {toolName}");
            System.Console.ResetColor();
            System.Console.WriteLine("     1) Approve this call");
            System.Console.WriteLine("     2) Always approve this tool (any arguments)");
            System.Console.WriteLine("     3) Always approve this tool with these arguments");
            System.Console.WriteLine("     4) Deny");
            System.Console.Write("     Choice [1-4]: ");

            string? choice = System.Console.ReadLine()?.Trim();
            AIContent response = choice switch
            {
                "2" => request.CreateAlwaysApproveToolResponse("User chose to always approve this tool"),
                "3" => request.CreateAlwaysApproveToolWithArgumentsResponse("User chose to always approve this tool with these arguments"),
                "4" => request.CreateResponse(approved: false, reason: "User denied"),
                _ => request.CreateResponse(approved: true, reason: "User approved"),
            };

            string action = choice switch
            {
                "2" => "✅ Always approved (any args)",
                "3" => "✅ Always approved (these args)",
                "4" => "❌ Denied",
                _ => "✅ Approved",
            };
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine($"     {action}");
            System.Console.ResetColor();

            responses.Add(response);
        }

        return [new ChatMessage(ChatRole.User, responses)];
    }

    private static void HandleModeCommand(AgentModeProvider? modeProvider, AgentSession session, string input)
    {
        if (modeProvider is null)
        {
            System.Console.WriteLine("AgentModeProvider is not available.");
            return;
        }

        string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            string current = modeProvider.GetMode(session);
            System.Console.WriteLine($"\n  Current mode: {current}\n");
            return;
        }

        string newMode = parts[1];

        try
        {
            modeProvider.SetMode(session, newMode);
            System.Console.ForegroundColor = GetModeColor(newMode);
            System.Console.WriteLine($"\n  Switched to {newMode} mode.\n");
            System.Console.ResetColor();
        }
        catch (ArgumentException ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"\n  {ex}\n");
            System.Console.ResetColor();
        }
    }

    private static void WritePrompt(AgentModeProvider? modeProvider, AgentSession session)
    {
        string mode = modeProvider?.GetMode(session) ?? "unknown";
        System.Console.ForegroundColor = GetModeColor(mode);
        System.Console.Write($"[{mode}] You: ");
        System.Console.ResetColor();
    }

    private static void PrintTodos(TodoProvider? todoProvider, AgentSession session)
    {
        if (todoProvider is null)
        {
            System.Console.WriteLine("TodoProvider is not available.");
            return;
        }

        var todos = todoProvider.GetAllTodos(session);
        if (todos.Count == 0)
        {
            System.Console.WriteLine("\n  No todos yet.\n");
            return;
        }

        System.Console.WriteLine();
        System.Console.WriteLine("  ── Todo List ──");
        foreach (var item in todos)
        {
            string status = item.IsComplete ? "✓" : "○";
            System.Console.ForegroundColor = item.IsComplete ? ConsoleColor.DarkGray : ConsoleColor.White;
            System.Console.Write($"  [{status}] #{item.Id} {item.Title}");
            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                System.Console.Write($" — {item.Description}");
            }

            System.Console.WriteLine();
        }

        System.Console.ResetColor();
        System.Console.WriteLine();
    }

    private static void WriteUsageBreakdown(UsageDetails details, int? maxContextWindowTokens, int? maxOutputTokens)
    {
        int? inputBudget = (maxContextWindowTokens is not null && maxOutputTokens is not null)
            ? maxContextWindowTokens.Value - maxOutputTokens.Value
            : null;

        System.Console.Write(" — input: ");
        WriteTokenCount(details.InputTokenCount, inputBudget);

        System.Console.Write(" | output: ");
        WriteTokenCount(details.OutputTokenCount, maxOutputTokens);

        System.Console.Write(" | total: ");
        WriteTokenCount(details.TotalTokenCount, maxContextWindowTokens);
    }

    private static void WriteTokenCount(long? count, int? budget)
    {
        if (count is null)
        {
            System.Console.Write("—");
            return;
        }

        System.Console.Write($"{count.Value:N0}");
        if (budget is not null && budget.Value > 0)
        {
            double pct = (double)count.Value / budget.Value * 100;
            System.Console.Write($"/{budget.Value:N0} ({pct:F1}%)");
        }
    }

    private static ConsoleColor GetModeColor(string mode) => mode switch
    {
        "plan" => ConsoleColor.Cyan,
        "execute" => ConsoleColor.Green,
        _ => ConsoleColor.Gray,
    };
}
