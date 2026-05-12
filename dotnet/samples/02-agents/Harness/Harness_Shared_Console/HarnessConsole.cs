// Copyright (c) Microsoft. All rights reserved.

using Harness.ConsoleReactiveComponents;
using Harness.Shared.Console.Commands;
using Harness.Shared.Console.Observers;
using Microsoft.Agents.AI;

namespace Harness.Shared.Console;

/// <summary>
/// Provides a reusable interactive console loop for running an <see cref="AIAgent"/>
/// with streaming output, extensible observers, and mode-aware interaction strategies.
/// </summary>
public static class HarnessConsole
{
    /// <summary>
    /// Runs an interactive console session with the specified agent.
    /// Constructs the reactive UI component and the <see cref="HarnessAgentRunner"/>,
    /// wires them together, and awaits the runner's <see cref="HarnessAgentRunner.ShutdownTask"/>
    /// (which completes when the user types <c>exit</c>).
    /// </summary>
    /// <param name="agent">The agent to interact with.</param>
    /// <param name="userPrompt">A short prompt to the user, displayed as a placeholder in the input area.</param>
    /// <param name="options">Optional configuration options for the console session.</param>
    public static async Task RunAgentAsync(AIAgent agent, string userPrompt, HarnessConsoleOptions? options = null)
    {
        options ??= new();

        if (options.EnablePlanningUx
            && (string.IsNullOrWhiteSpace(options.PlanningModeName) || string.IsNullOrWhiteSpace(options.ExecutionModeName)))
        {
            throw new ArgumentException(
                "When EnablePlanningUx is true, both PlanningModeName and ExecutionModeName must be configured.",
                nameof(options));
        }

        var todoProvider = agent.GetService<TodoProvider>();
        var modeProvider = agent.GetService<AgentModeProvider>();
        var messageInjector = agent.GetService<MessageInjectingChatClient>();

        var commandHandlers = new List<CommandHandler>
        {
            new TodoCommandHandler(todoProvider),
            new ModeCommandHandler(modeProvider, options.ModeColors),
        };

        AgentSession session = await agent.CreateSessionAsync();

        var observers = CreateObservers(options, modeProvider, session);

        using var component = new HarnessAppComponent(
            placeholder: userPrompt,
            initialMode: modeProvider?.GetMode(session),
            inputEnabled: messageInjector is not null,
            runnerFactory: ux => new HarnessAgentRunner(
                agent: agent,
                session: session,
                modeProvider: modeProvider,
                messageInjector: messageInjector,
                options: options,
                commandHandlers: commandHandlers,
                observers: observers,
                ux: ux),
            modeColors: options.ModeColors);

        // Trigger the initial render of the component now that state is seeded.
        component.Render();

        try
        {
            await component.Runner.ShutdownTask.ConfigureAwait(false);
        }
        finally
        {
            component.Deactivate();
        }

        System.Console.ResetColor();
        System.Console.WriteLine(AnsiEscapes.EraseEntireScreen);
        System.Console.WriteLine(AnsiEscapes.EraseScrollbackBuffer);
        System.Console.WriteLine("Goodbye!");
    }

    private static List<ConsoleObserver> CreateObservers(HarnessConsoleOptions options, AgentModeProvider? modeProvider, AgentSession session)
    {
        var observers = new List<ConsoleObserver>
        {
            new ToolCallDisplayObserver(),
            new ToolApprovalObserver(),
            new ErrorDisplayObserver(),
            new ReasoningDisplayObserver(),
            new UsageDisplayObserver(options.MaxContextWindowTokens, options.MaxOutputTokens),
        };

        if (options.EnablePlanningUx
            && modeProvider is not null
            && string.Equals(modeProvider.GetMode(session), options.PlanningModeName, StringComparison.OrdinalIgnoreCase))
        {
            observers.Add(new PlanningOutputObserver(modeProvider));
        }
        else
        {
            observers.Add(new TextOutputObserver());
        }

        return observers;
    }
}
