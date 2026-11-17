// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI.A2A;

/// <summary>
/// Represents configuration options for an <see cref="A2AAgent"/>, including its identifier, name, and description.
/// </summary>
/// <remarks>
/// <para>
/// This class is used to encapsulate information about an A2A agent, such as its unique
/// identifier, display name, and a descriptive summary.
/// </para>
/// </remarks>
public sealed class A2AAgentOptions
{
    /// <summary>
    /// Gets or sets the agent id.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the agent name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the agent description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an agent card is allowed to direct requests to
    /// addresses that use plain <c>http</c>.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/>, which accepts only <c>https</c> addresses. Plain <c>http</c> sends
    /// requests and credentials unencrypted, so enable it only for local development or trusted networks.
    /// </remarks>
    [Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
    public bool AllowAgentCardHostsInsecureHttp { get; set; }

    /// <summary>
    /// Creates a new instance of <see cref="A2AAgentOptions"/> with the same values as this instance.
    /// </summary>
    public A2AAgentOptions Clone()
        => new()
        {
            Id = this.Id,
            Name = this.Name,
            Description = this.Description,
            AllowAgentCardHostsInsecureHttp = this.AllowAgentCardHostsInsecureHttp
        };
}
