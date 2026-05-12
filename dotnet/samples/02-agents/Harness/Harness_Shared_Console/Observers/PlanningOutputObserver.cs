// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Harness.Shared.Console.Observers;

/// <summary>
/// Planning observer that configures structured output, collects streamed text,
/// and deserializes it as a <see cref="PlanningResponse"/>. After the stream completes,
/// it returns one <see cref="FollowUpQuestion"/> per question to ask the user.
/// Approval continuations also handle the mode switch when the user approves the plan.
/// </summary>
internal sealed class PlanningOutputObserver : ConsoleObserver
{
    private readonly StringBuilder _textCollector = new();
    private readonly AgentModeProvider _modeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanningOutputObserver"/> class.
    /// </summary>
    /// <param name="modeProvider">The mode provider for switching modes on approval.</param>
    public PlanningOutputObserver(AgentModeProvider modeProvider)
    {
        this._modeProvider = modeProvider;
    }

    /// <inheritdoc/>
    public override void ConfigureRunOptions(AgentRunOptions options)
    {
        options.ResponseFormat = ChatResponseFormat.ForJsonSchema<PlanningResponse>();
    }

    /// <inheritdoc/>
    public override Task OnTextAsync(IUXStateDriver ux, string text)
    {
        // Collect text silently instead of displaying it.
        this._textCollector.Append(text);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task<IList<FollowUpAction>?> OnStreamCompleteAsync(
        IUXStateDriver ux,
        AIAgent agent,
        AgentSession session,
        HarnessConsoleOptions options)
    {
        // Read collected text from our stream observation.
        string collectedText = this._textCollector.ToString();
        this._textCollector.Clear();

        if (string.IsNullOrWhiteSpace(collectedText))
        {
            return null;
        }

        // Deserialize the structured response.
        PlanningResponse? planningResponse;
        try
        {
            planningResponse = JsonSerializer.Deserialize<PlanningResponse>(collectedText);
        }
        catch (JsonException ex)
        {
            await ux.WriteInfoLineAsync($"❌ Failed to parse planning response: {ex.Message}", ConsoleColor.Red);
            await ux.WriteInfoLineAsync($"(raw response) {collectedText}", ConsoleColor.DarkYellow);
            return null;
        }

        if (planningResponse is null)
        {
            await ux.WriteInfoLineAsync("(no structured response from agent)", ConsoleColor.DarkYellow);
            return null;
        }

        if (planningResponse.Type == PlanningResponseType.Clarification)
        {
            return BuildClarificationActions(planningResponse);
        }

        if (planningResponse.Type == PlanningResponseType.Approval)
        {
            var question = planningResponse.Questions.FirstOrDefault();
            if (question is null)
            {
                await ux.WriteInfoLineAsync("(approval response had no content)", ConsoleColor.DarkYellow);
                return null;
            }

            return new List<FollowUpAction> { this.BuildApprovalAction(question, options, session) };
        }

        await ux.WriteInfoLineAsync($"(unexpected response type: {planningResponse.Type})", ConsoleColor.DarkYellow);
        return null;
    }

    private static List<FollowUpAction> BuildClarificationActions(PlanningResponse response)
    {
        var actions = new List<FollowUpAction>(response.Questions.Count);

        foreach (var question in response.Questions)
        {
            string prompt = question.Message;

            async Task<ChatMessage?> Continuation(string answer, IUXStateDriver ux)
            {
                await ux.WriteInfoLineAsync($"Q: {prompt}", ConsoleColor.Gray).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(answer))
                {
                    await ux.WriteInfoLineAsync("A: (no answer)", ConsoleColor.DarkGray).ConfigureAwait(false);
                    return null;
                }

                await ux.WriteInfoLineAsync($"A: {answer}", ConsoleColor.Green).ConfigureAwait(false);

                string formatted = $"Q: {prompt}\nA: {answer}";
                return new ChatMessage(ChatRole.User, formatted);
            }

            if (question.Choices is { Count: > 0 })
            {
                actions.Add(new ChoiceFollowUpQuestion(
                    Prompt: prompt,
                    Choices: question.Choices,
                    AllowCustomText: true,
                    Continuation: Continuation));
            }
            else
            {
                actions.Add(new TextFollowUpQuestion(
                    Prompt: prompt,
                    Continuation: Continuation));
            }
        }

        return actions;
    }

    private ChoiceFollowUpQuestion BuildApprovalAction(PlanningQuestion question, HarnessConsoleOptions options, AgentSession session)
    {
        const string ApproveOption = "Approve and switch to execute mode";
        var choices = new List<string> { ApproveOption };

        return new ChoiceFollowUpQuestion(
            Prompt: question.Message,
            Choices: choices,
            AllowCustomText: true,
            Continuation: async (selection, ux) =>
            {
                await ux.WriteInfoLineAsync($"Q: {question.Message}", ConsoleColor.Gray).ConfigureAwait(false);
                await ux.WriteInfoLineAsync($"A: {selection}", ConsoleColor.Green).ConfigureAwait(false);

                if (selection == ApproveOption)
                {
                    this._modeProvider.SetMode(session, options.ExecutionModeName!);
                    await ux.WriteInfoLineAsync(
                        $"✅ Switched to {options.ExecutionModeName} mode.",
                        ModeColors.Get(options.ExecutionModeName, options.ModeColors)).ConfigureAwait(false);
                    return new ChatMessage(ChatRole.User, "Approved");
                }

                // Custom freeform input — treat as suggested changes.
                return new ChatMessage(ChatRole.User, selection);
            });
    }
}
