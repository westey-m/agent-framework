// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI;

/// <summary>
/// An <see cref="AIContextProvider"/> that tracks the agent's operating mode (e.g., "plan" or "execute")
/// in the session state and provides tools for querying and switching modes.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="AgentModeProvider"/> enables agents to operate in distinct modes during long-running
/// complex tasks. The current mode is persisted in the session's <see cref="AgentSessionStateBag"/>
/// and is included in the instructions provided to the agent on each invocation.
/// </para>
/// <para>
/// This provider exposes the following tools to the agent:
/// <list type="bullet">
/// <item><description><c>SetMode</c> — Switch the agent's operating mode.</description></item>
/// <item><description><c>GetMode</c> — Retrieve the agent's current operating mode.</description></item>
/// </list>
/// </para>
/// <para>
/// Public helper methods <see cref="GetMode"/> and <see cref="SetMode"/> allow external code
/// to programmatically read and change the mode.
/// </para>
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public sealed class AgentModeProvider : AIContextProvider
{
    /// <summary>
    /// The "plan" mode, indicating the agent is planning work.
    /// </summary>
    public const string ModePlan = "plan";

    /// <summary>
    /// The "execute" mode, indicating the agent is executing work.
    /// </summary>
    public const string ModeExecute = "execute";

    private readonly ProviderSessionState<AgentModeState> _sessionState;
    private IReadOnlyList<string>? _stateKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentModeProvider"/> class.
    /// </summary>
    public AgentModeProvider()
    {
        this._sessionState = new ProviderSessionState<AgentModeState>(
            _ => new AgentModeState(),
            this.GetType().Name,
            AgentJsonUtilities.DefaultOptions);
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => this._stateKeys ??= [this._sessionState.StateKey];

    /// <summary>
    /// Gets the current operating mode from the session state.
    /// </summary>
    /// <param name="session">The agent session to read the mode from.</param>
    /// <returns>The current mode string.</returns>
    public string GetMode(AgentSession? session)
    {
        return this._sessionState.GetOrInitializeState(session).CurrentMode;
    }

    /// <summary>
    /// Sets the operating mode in the session state.
    /// </summary>
    /// <param name="session">The agent session to update the mode in.</param>
    /// <param name="mode">The new mode to set.</param>
    public void SetMode(AgentSession? session, string mode)
    {
        AgentModeState state = this._sessionState.GetOrInitializeState(session);
        state.CurrentMode = mode;
        this._sessionState.SaveState(session, state);
    }

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        AgentModeState state = this._sessionState.GetOrInitializeState(context.Session);

        string instructions = $"""
            You are currently operating in "{state.CurrentMode}" mode.
            Available modes:
            - "plan": Use this mode when analyzing requirements, breaking down tasks, and creating plans.
            - "execute": Use this mode when implementing changes, writing code, and carrying out planned work.
            Use the SetMode tool to switch between modes as your work progresses. Only use SetMode if the user explicitly instructs you to change modes.
            Use the GetMode tool to check your current operating mode.
            """;

        return new ValueTask<AIContext>(new AIContext
        {
            Instructions = instructions,
            Tools = this.CreateTools(state, context.Session),
        });
    }

    private AITool[] CreateTools(AgentModeState state, AgentSession? session)
    {
        var serializerOptions = AgentJsonUtilities.DefaultOptions;

        return
        [
            AIFunctionFactory.Create(
                (string mode) =>
                {
                    state.CurrentMode = mode;
                    this._sessionState.SaveState(session, state);
                    return $"Mode changed to \"{mode}\".";
                },
                new AIFunctionFactoryOptions
                {
                    Name = "SetMode",
                    Description = "Switch the agent's operating mode. Supported modes: \"plan\" and \"execute\".",
                    SerializerOptions = serializerOptions,
                }),

            AIFunctionFactory.Create(
                () => state.CurrentMode,
                new AIFunctionFactoryOptions
                {
                    Name = "GetMode",
                    Description = "Get the agent's current operating mode.",
                    SerializerOptions = serializerOptions,
                }),
        ];
    }
}
