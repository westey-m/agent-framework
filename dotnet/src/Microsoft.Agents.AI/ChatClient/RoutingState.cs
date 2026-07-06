// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI;

/// <summary>
/// Represents the serializable routing state of a <see cref="RoutingChatClient"/>,
/// stored in the session's <see cref="AgentSessionStateBag"/>.
/// </summary>
/// <remarks>
/// This state tracks the currently active destination for a session. Use it from a custom
/// <c>stateInitializer</c> to control which inner client a new session is routed to by default.
/// </remarks>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public sealed class RoutingState
{
    /// <summary>
    /// Gets or sets the key of the currently active destination for this session, or <see langword="null"/>
    /// to use the default destination.
    /// </summary>
    /// <remarks>
    /// When non-<see langword="null"/>, the value corresponds to a key in the inner clients dictionary supplied
    /// to the <see cref="RoutingChatClient"/> (or a key handled by a fallback factory). It is used by the default
    /// router to select a destination. When <see langword="null"/>, the request is routed to the first inner
    /// client (the default destination).
    /// </remarks>
    [JsonPropertyName("activeDestination")]
    public string? ActiveDestination { get; set; }
}
