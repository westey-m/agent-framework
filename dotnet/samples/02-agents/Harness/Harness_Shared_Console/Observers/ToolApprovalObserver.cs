// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Harness.Shared.Console.Observers;

/// <summary>
/// Collects <see cref="ToolApprovalRequestContent"/> items during the response stream,
/// displays approval-needed notifications inline, and after the stream completes returns
/// one <see cref="ChoiceFollowUpQuestion"/> per pending approval request. Each question's
/// continuation produces a separate <see cref="ChatMessage"/> carrying the approval
/// response content.
/// </summary>
internal sealed class ToolApprovalObserver : ConsoleObserver
{
    private readonly List<ToolApprovalRequestContent> _approvalRequests = [];

    /// <inheritdoc/>
    public override async Task OnContentAsync(IUXStateDriver ux, AIContent content)
    {
        if (content is ToolApprovalRequestContent approvalRequest)
        {
            this._approvalRequests.Add(approvalRequest);
            string toolName = approvalRequest.ToolCall is FunctionCallContent fc
                ? ToolCallFormatter.Format(fc)
                : approvalRequest.ToolCall?.ToString() ?? "unknown";
            await ux.WriteInfoLineAsync($"⚠️ Approval needed: {toolName}", ConsoleColor.Yellow);
        }
    }

    /// <inheritdoc/>
    public override Task<IList<FollowUpAction>?> OnStreamCompleteAsync(
        IUXStateDriver ux,
        AIAgent agent,
        AgentSession session,
        HarnessConsoleOptions options)
    {
        if (this._approvalRequests.Count == 0)
        {
            return Task.FromResult<IList<FollowUpAction>?>(null);
        }

        var actions = new List<FollowUpAction>(this._approvalRequests.Count);
        foreach (var request in this._approvalRequests)
        {
            actions.Add(BuildApprovalQuestion(request));
        }

        this._approvalRequests.Clear();
        return Task.FromResult<IList<FollowUpAction>?>(actions);
    }

    private static ChoiceFollowUpQuestion BuildApprovalQuestion(ToolApprovalRequestContent request)
    {
        string toolName = request.ToolCall is FunctionCallContent fc
            ? ToolCallFormatter.Format(fc)
            : request.ToolCall?.ToString() ?? "unknown";

        var choices = new List<string>
        {
            "Approve this call",
            "Always approve this tool (any arguments)",
            "Always approve this tool with these arguments",
            "Deny",
        };

        string prompt = $"🔐 Tool approval: {toolName}";

        return new ChoiceFollowUpQuestion(
            Prompt: prompt,
            Choices: choices,
            AllowCustomText: false,
            Continuation: async (selection, ux) =>
            {
                AIContent response = selection switch
                {
                    "Always approve this tool (any arguments)" => request.CreateAlwaysApproveToolResponse("User chose to always approve this tool"),
                    "Always approve this tool with these arguments" => request.CreateAlwaysApproveToolWithArgumentsResponse("User chose to always approve this tool with these arguments"),
                    "Deny" => request.CreateResponse(approved: false, reason: "User denied"),
                    _ => request.CreateResponse(approved: true, reason: "User approved"),
                };

                string action = selection switch
                {
                    "Always approve this tool (any arguments)" => "✅ Always approved (any args)",
                    "Always approve this tool with these arguments" => "✅ Always approved (these args)",
                    "Deny" => "❌ Denied",
                    _ => "✅ Approved",
                };

                ConsoleColor answerColor = selection == "Deny" ? ConsoleColor.Red : ConsoleColor.Green;
                await ux.WriteInfoLineAsync($"Q: {prompt}", ConsoleColor.Gray).ConfigureAwait(false);
                await ux.WriteInfoLineAsync($"A: {action}", answerColor).ConfigureAwait(false);

                return new ChatMessage(ChatRole.User, [response]);
            });
    }
}
