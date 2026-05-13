// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using Microsoft.Extensions.AI;

namespace Harness.Shared.Console.ToolFormatters;

/// <summary>
/// Formats <c>SubAgents_*</c> tool calls with human-readable details
/// for task start, continue, wait, and result retrieval operations.
/// </summary>
public sealed class SubAgentToolFormatter : ToolCallFormatter
{
    /// <inheritdoc/>
    public override bool CanFormat(FunctionCallContent call) => call.Name.StartsWith("SubAgents_", StringComparison.Ordinal);

    /// <inheritdoc/>
    public override string? FormatDetail(FunctionCallContent call) => call.Name switch
    {
        "SubAgents_StartTask" => FormatStartSubTask(call),
        "SubAgents_WaitForFirstCompletion" => FormatIdList(call, "taskIds", "Wait for"),
        "SubAgents_GetTaskResults" => FormatSingleId(call, "taskId"),
        "SubAgents_ContinueTask" => FormatContinueTask(call),
        "SubAgents_ClearCompletedTask" => FormatSingleId(call, "taskId"),
        _ => null,
    };

    private static string? FormatStartSubTask(FunctionCallContent call)
    {
        string? agentName = GetStringArgumentValue(call, "agentName");
        string? description = GetStringArgumentValue(call, "description");

        if (agentName is null && description is null)
        {
            return null;
        }

        var sb = new StringBuilder("(");
        if (agentName is not null)
        {
            sb.Append($"agent: {agentName}");
        }

        if (description is not null)
        {
            if (agentName is not null)
            {
                sb.Append(", ");
            }

            sb.Append($"\"{Truncate(description, 60)}\"");
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string? FormatIdList(FunctionCallContent call, string paramName, string verb)
    {
        List<int>? ids = GetIntListArgumentValue(call, paramName);
        if (ids is null || ids.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < ids.Count; i++)
        {
            string connector = i < ids.Count - 1 ? "├─" : "└─";
            sb.Append($"\n   {connector} {verb} #{ids[i]}");
        }

        return sb.ToString();
    }

    private static string? FormatSingleId(FunctionCallContent call, string paramName)
    {
        int? id = GetIntArgumentValue(call, paramName);
        return id.HasValue ? $"(task #{id.Value})" : null;
    }

    private static string? FormatContinueTask(FunctionCallContent call)
    {
        int? taskId = GetIntArgumentValue(call, "taskId");
        string? text = GetStringArgumentValue(call, "text");

        if (!taskId.HasValue)
        {
            return null;
        }

        return text is not null
            ? $"(task #{taskId.Value}, \"{Truncate(text, 50)}\")"
            : $"(task #{taskId.Value})";
    }
}
