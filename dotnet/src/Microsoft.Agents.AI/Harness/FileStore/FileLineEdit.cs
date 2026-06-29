// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Shared.DiagnosticIds;

namespace Microsoft.Agents.AI;

/// <summary>
/// Represents a single whole-line replacement used by the file access and file memory
/// <c>replace_lines</c> tools.
/// </summary>
[Experimental(DiagnosticIds.Experiments.AgentsAIExperiments)]
public sealed class FileLineEdit
{
    /// <summary>
    /// Gets or sets the 1-based line number to replace.
    /// </summary>
    [JsonPropertyName("line_number")]
    [Description("1-based line number to replace.")]
    public int LineNumber { get; set; }

    /// <summary>
    /// Gets or sets the replacement content for the whole line (no trailing newline).
    /// </summary>
    [JsonPropertyName("new_line")]
    [Description("Replacement content for the whole line (no trailing newline).")]
    public string NewLine { get; set; } = string.Empty;
}
