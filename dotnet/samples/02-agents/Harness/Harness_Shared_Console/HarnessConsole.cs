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
    public static async Task RunAgentAsync(AIAgent agent, string title, string userPrompt)
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
                await StreamAgentResponseAsync(agent, session, modeProvider, userInput);
            }

            WritePrompt(modeProvider, session);
            userInput = System.Console.ReadLine();
        }

        System.Console.ResetColor();
        System.Console.WriteLine("Goodbye!");
    }

    private static async Task StreamAgentResponseAsync(AIAgent agent, AgentSession session, AgentModeProvider? modeProvider, string userInput)
    {
        string mode = modeProvider?.GetMode(session) ?? "unknown";
        System.Console.ForegroundColor = GetModeColor(mode);
        System.Console.Write($"\n[{mode}] Agent: ");

        var spinner = new Spinner();
        spinner.Start();
        bool hasTextOutput = false;
        bool hasReceivedAnyText = false;

        try
        {
            await foreach (var update in agent.RunStreamingAsync(userInput, session))
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
            System.Console.Write($"\n  ❌ Stream error: {ex.GetType().Name}: {ex.Message}");
        }

        await spinner.StopAsync();

        if (!hasReceivedAnyText)
        {
            System.Console.ForegroundColor = ConsoleColor.DarkYellow;
            System.Console.Write("\n  (no text response from agent)");
        }

        System.Console.ResetColor();
        System.Console.WriteLine();
        System.Console.WriteLine();
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

        // Normalize to known mode values for case-insensitive matching.
        if (string.Equals(newMode, AgentModeProvider.PlanMode, StringComparison.OrdinalIgnoreCase))
        {
            newMode = AgentModeProvider.PlanMode;
        }
        else if (string.Equals(newMode, AgentModeProvider.ExecuteMode, StringComparison.OrdinalIgnoreCase))
        {
            newMode = AgentModeProvider.ExecuteMode;
        }

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
            System.Console.WriteLine($"\n  {ex.Message}\n");
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

    private static ConsoleColor GetModeColor(string mode) => mode switch
    {
        AgentModeProvider.PlanMode => ConsoleColor.Cyan,
        AgentModeProvider.ExecuteMode => ConsoleColor.Green,
        _ => ConsoleColor.Gray,
    };
}
