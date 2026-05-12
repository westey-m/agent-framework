// Copyright (c) Microsoft. All rights reserved.

using Harness.ConsoleReactiveComponents;
using Microsoft.Extensions.AI;

namespace Harness.Shared.Console;

/// <summary>
/// Default <see cref="IUXStateDriver"/> implementation. Owned by
/// <see cref="HarnessAppComponent"/>; mutates the component's state via a
/// <c>SetState</c>-style callback. Each public operation updates state and lets
/// the component's render-skip optimization handle the actual draw.
/// </summary>
internal sealed class HarnessConsoleUXStateDriver : IUXStateDriver
{
    private readonly Func<HarnessAppComponentState> _getState;
    private readonly Action<HarnessAppComponentState> _setState;
    private readonly IReadOnlyDictionary<string, ConsoleColor>? _modeColors;
    private readonly List<string> _outputItems = [];
    private readonly object _outputLock = new();

    private OutputEntryType? _lastEntryType;
    private bool _hasReceivedAnyText;
    private OutputEntry? _currentStreamingEntry;
    private string? _currentMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessConsoleUXStateDriver"/> class.
    /// </summary>
    /// <param name="getState">Returns the component's current state.</param>
    /// <param name="setState">Replaces the component's state and triggers a re-render.</param>
    /// <param name="modeColors">Optional mapping of mode names to console colors.</param>
    public HarnessConsoleUXStateDriver(
        Func<HarnessAppComponentState> getState,
        Action<HarnessAppComponentState> setState,
        IReadOnlyDictionary<string, ConsoleColor>? modeColors = null)
    {
        this._getState = getState;
        this._setState = setState;
        this._modeColors = modeColors;
        this._currentMode = getState().ModeText;
    }

    /// <inheritdoc/>
    public string? CurrentMode
    {
        get => this._currentMode;
        set
        {
            this._currentMode = value;
            this.UpdateState(s => s with
            {
                ModeColor = ModeColors.Get(value, this._modeColors),
                ModeText = value,
            });
        }
    }

    /// <inheritdoc/>
    public void BeginStreaming() =>
        this.UpdateState(s => s with
        {
            Mode = BottomPanelMode.Streaming,
            ShowSpinner = true,
        });

    /// <inheritdoc/>
    public void StopSpinner() =>
        this.UpdateState(s => s with { ShowSpinner = false });

    /// <inheritdoc/>
    public void EndStreaming() =>
        this.UpdateState(s => s with
        {
            Mode = BottomPanelMode.TextInput,
            ShowSpinner = false,
        });

    /// <inheritdoc/>
    public void BeginStreamingOutput()
    {
        lock (this._outputLock)
        {
            this._hasReceivedAnyText = false;
            this._currentStreamingEntry = null;
        }
    }

    /// <inheritdoc/>
    public void SetUsageText(string usageText) =>
        this.UpdateState(s => s with { UsageText = usageText });

    /// <inheritdoc/>
    public void SetQueuedMessages(IReadOnlyList<ChatMessage> pending)
    {
        var newQueued = new List<string>(pending.Count);
        foreach (var msg in pending)
        {
            string text = msg.Text ?? string.Empty;
            newQueued.Add(RenderEntry($"  💬 {text}\n", ConsoleColor.DarkGray));
        }

        this.UpdateState(s => s with { QueuedItems = newQueued });
    }

    /// <inheritdoc/>
    public void QueueFollowUpQuestions(IReadOnlyList<FollowUpQuestion> questions)
    {
        if (questions.Count == 0)
        {
            return;
        }

        HarnessAppComponentState current = this._getState();
        bool wasEmpty = current.PendingQuestions.Count == 0;

        var combined = new List<FollowUpQuestion>(current.PendingQuestions.Count + questions.Count);
        combined.AddRange(current.PendingQuestions);
        combined.AddRange(questions);

        HarnessAppComponentState next = current with { PendingQuestions = combined };

        if (wasEmpty)
        {
            // The new head needs its display configured. Render any prompt-as-info-line
            // side effects first (so the resulting state covers display fields too).
            next = this.ConfigureForHeadQuestion(next, combined[0]);
        }

        this._setState(next);
    }

    /// <inheritdoc/>
    public void AddFollowUpResponse(ChatMessage response)
    {
        this.UpdateState(s =>
        {
            var combined = new List<ChatMessage>(s.AccumulatedFollowUpResponses.Count + 1);
            combined.AddRange(s.AccumulatedFollowUpResponses);
            combined.Add(response);
            return s with { AccumulatedFollowUpResponses = combined };
        });
    }

    /// <inheritdoc/>
    public void AdvanceFollowUpQuestion()
    {
        HarnessAppComponentState current = this._getState();
        if (current.PendingQuestions.Count == 0)
        {
            return;
        }

        var remaining = current.PendingQuestions.Skip(1).ToList();
        HarnessAppComponentState next = current with { PendingQuestions = remaining };

        if (remaining.Count > 0)
        {
            next = this.ConfigureForHeadQuestion(next, remaining[0]);
        }
        else
        {
            next = next with
            {
                Mode = BottomPanelMode.TextInput,
                ListSelectionOptions = [],
                ListSelectionTitle = null,
                ListSelectionCustomTextPlaceholder = null,
                ListSelectionIndex = 0,
                ListSelectionCustomInputText = "",
            };
        }

        this._setState(next);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ChatMessage> TakeFollowUpResponses()
    {
        HarnessAppComponentState current = this._getState();
        IReadOnlyList<ChatMessage> responses = current.AccumulatedFollowUpResponses;
        if (responses.Count == 0)
        {
            return responses;
        }

        this._setState(current with { AccumulatedFollowUpResponses = [] });
        return responses;
    }

    /// <summary>
    /// Configures the bottom-panel display fields on the supplied state for the
    /// given head question. For text questions, also writes the prompt as an
    /// info line above the input row as a side effect.
    /// </summary>
    private HarnessAppComponentState ConfigureForHeadQuestion(HarnessAppComponentState state, FollowUpQuestion question)
    {
        if (question is ChoiceFollowUpQuestion choice)
        {
            return state with
            {
                Mode = BottomPanelMode.ListSelection,
                ListSelectionOptions = choice.Choices.ToList(),
                ListSelectionTitle = choice.Prompt,
                ListSelectionCustomTextPlaceholder = choice.AllowCustomText ? "✏️  Type a custom response..." : null,
                ListSelectionIndex = 0,
                ListSelectionCustomInputText = "",
            };
        }

        // Text question — prompt is rendered as an info line above the input row.
        ConsoleColor ruleColor = ModeColors.Get(this._currentMode, this._modeColors);
        this.AppendOutputEntries(
            new OutputEntry(OutputEntryType.InfoLine, "\n", ruleColor),
            new OutputEntry(OutputEntryType.InfoLine, $"  {question.Prompt}", ruleColor));

        return state with
        {
            Mode = BottomPanelMode.TextInput,
            ListSelectionOptions = [],
            ListSelectionTitle = null,
            ListSelectionCustomTextPlaceholder = null,
            ListSelectionIndex = 0,
            ListSelectionCustomInputText = "",
        };
    }

    /// <inheritdoc/>
    public void WriteUserInputEcho(string text)
    {
        this.AppendOutputEntries(new OutputEntry(
            OutputEntryType.UserInput,
            $"\nYou: {text}\n",
            ConsoleColor.Green));
    }

    /// <inheritdoc/>
    public Task WriteInfoAsync(string text, ConsoleColor? color = null) =>
        this.WriteInfoCoreAsync(text, color, newLine: false);

    /// <inheritdoc/>
    public Task WriteInfoLineAsync(string text, ConsoleColor? color = null) =>
        this.WriteInfoCoreAsync(text, color, newLine: true);

    private Task WriteInfoCoreAsync(string text, ConsoleColor? color, bool newLine)
    {
        // Add a blank line separator when transitioning from streaming text or user input.
        string prefix = this._lastEntryType is OutputEntryType.StreamingText or OutputEntryType.StreamFooter
            ? "\n\n  "
            : "  ";

        string fullText = newLine ? prefix + text + "\n" : prefix + text;
        this.AppendOutputEntries(new OutputEntry(
            OutputEntryType.InfoLine,
            fullText,
            color ?? ModeColors.Get(this._currentMode, this._modeColors)));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task WriteTextAsync(string text, ConsoleColor? color = null)
    {
        lock (this._outputLock)
        {
            this._lastEntryType = OutputEntryType.StreamingText;
            this._hasReceivedAnyText = true;

            ConsoleColor effectiveColor = color ?? ModeColors.Get(this._currentMode, this._modeColors);

            if (this._currentStreamingEntry is not null)
            {
                this._currentStreamingEntry = this._currentStreamingEntry with
                {
                    Text = this._currentStreamingEntry.Text + text,
                };
                this._outputItems[^1] = RenderEntry(this._currentStreamingEntry.Text, this._currentStreamingEntry.Color);
            }
            else
            {
                const string Prefix = "\n";
                this._currentStreamingEntry = new OutputEntry(OutputEntryType.StreamingText, Prefix + text, effectiveColor);
                this._outputItems.Add(RenderEntry(this._currentStreamingEntry.Text, this._currentStreamingEntry.Color));
            }

            var snapshot = new List<string>(this._outputItems);
            this._setState(this._getState() with { ScrollAreaContentItems = snapshot });
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task EndStreamingOutputAsync()
    {
        lock (this._outputLock)
        {
            this._outputItems.Add(RenderEntry("\n", null));
            this._currentStreamingEntry = null;
            this._lastEntryType = OutputEntryType.StreamFooter;
            var snapshot = new List<string>(this._outputItems);
            this._setState(this._getState() with { ScrollAreaContentItems = snapshot });
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task WriteNoTextWarningAsync(bool hasFollowUpActions)
    {
        if (!this._hasReceivedAnyText && !hasFollowUpActions)
        {
            this.AppendOutputEntries(new OutputEntry(
                OutputEntryType.StreamFooter,
                "  (no text response from agent)\n",
                ConsoleColor.DarkYellow));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Wraps the supplied text with ANSI foreground color escape sequences (or returns
    /// the text unchanged when no color is specified). Output is appended to
    /// <see cref="_outputItems"/> and consumed verbatim by <see cref="TextScrollPanel"/>
    /// and <see cref="TextPanel"/>.
    /// </summary>
    private static string RenderEntry(string text, ConsoleColor? color) =>
        color.HasValue
            ? $"{AnsiEscapes.SetForegroundColor(color.Value)}{text}{AnsiEscapes.ResetAttributes}"
            : text;

    private void UpdateState(Func<HarnessAppComponentState, HarnessAppComponentState> update)
    {
        this._setState(update(this._getState()));
    }

    /// <summary>
    /// Appends one or more output entries to the output list under lock,
    /// updates <see cref="_lastEntryType"/> to the last entry's type, and pushes
    /// a new state snapshot. Each entry is rendered to its final ANSI string before
    /// being stored in <see cref="_outputItems"/>.
    /// </summary>
    private void AppendOutputEntries(params OutputEntry[] entries)
    {
        lock (this._outputLock)
        {
            foreach (OutputEntry entry in entries)
            {
                this._outputItems.Add(RenderEntry(entry.Text, entry.Color));
            }

            if (entries.Length > 0)
            {
                this._lastEntryType = entries[^1].Type;
            }

            var snapshot = new List<string>(this._outputItems);
            this._setState(this._getState() with { ScrollAreaContentItems = snapshot });
        }
    }
}
