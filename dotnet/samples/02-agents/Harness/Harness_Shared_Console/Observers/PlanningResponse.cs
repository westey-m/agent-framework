// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Harness.Shared.Console.Observers;

/// <summary>
/// Represents a structured response from the agent while in planning mode.
/// Used with structured output to enable consistent rendering of clarification
/// questions and approval requests in the console.
/// </summary>
public class PlanningResponse
{
    /// <summary>
    /// Gets or sets the type of planning response.
    /// </summary>
    [JsonPropertyName("type")]
    public required PlanningResponseType Type { get; set; }

    /// <summary>
    /// Gets or sets the message to display to the user.
    /// For clarification, this is the question. For approval, this is a summary of what the agent plans to do.
    /// </summary>
    [JsonPropertyName("message")]
    [Description("For clarifications, this has the question that needs to be clarified with the user. For approvals, this would contain a summary of the execution plan that the user needs to approve.")]
    public required string Message { get; set; }

    /// <summary>
    /// Gets or sets the list of choices for the user to pick from.
    /// Only used when <see cref="Type"/> is <see cref="PlanningResponseType.Clarification"/>.
    /// </summary>
    [JsonPropertyName("choices")]
    [Description("For clarifications, this has a list of options that the user can choose from. null for approvals.")]
    public List<string>? Choices { get; set; }
}
