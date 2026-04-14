// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI;

/// <summary>
/// Represents the state of the agent's operating mode, stored in the session's <see cref="AgentSessionStateBag"/>.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
internal sealed class AgentModeState
{
    /// <summary>
    /// Gets or sets the current operating mode of the agent.
    /// </summary>
    [JsonPropertyName("currentMode")]
    public string CurrentMode { get; set; } = AgentModeProvider.ModePlan;
}
