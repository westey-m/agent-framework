// Copyright (c) Microsoft. All rights reserved.

using Harness.ConsoleReactiveComponents;
using Harness.ConsoleReactiveFramework;
using Harness.Shared.Console.Components;

namespace Harness.Shared.Console;

/// <summary>
/// Determines which component is shown in the bottom panel.
/// </summary>
public enum BottomPanelMode
{
    /// <summary>Show the text input component for user input.</summary>
    TextInput,

    /// <summary>Show the list selection component for interactive prompts.</summary>
    ListSelection,

    /// <summary>Show a disabled input indicator during agent streaming.</summary>
    Streaming,
}

/// <summary>
/// Event arguments for the <see cref="HarnessAppComponent.InputSubmitted"/> event.
/// </summary>
public sealed class InputSubmittedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InputSubmittedEventArgs"/> class.
    /// </summary>
    /// <param name="text">The submitted text.</param>
    /// <param name="mode">The bottom panel mode in which the input was submitted.</param>
    public InputSubmittedEventArgs(string text, BottomPanelMode mode)
    {
        this.Text = text;
        this.Mode = mode;
    }

    /// <summary>Gets the submitted text.</summary>
    public string Text { get; }

    /// <summary>Gets the bottom panel mode in which the input was submitted.</summary>
    public BottomPanelMode Mode { get; }
}

/// <summary>
/// Props for <see cref="HarnessAppComponent"/>.
/// </summary>
public class HarnessAppComponentProps : ConsoleReactiveProps
{
    /// <summary>Gets or sets the list selection choices (for ListSelection mode).</summary>
    public IReadOnlyList<string> Items { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the scroll items (output entries) to render in the scroll panel.</summary>
    public IReadOnlyList<object> ScrollItems { get; set; } = [];

    /// <summary>Gets or sets the bottom panel mode.</summary>
    public BottomPanelMode Mode { get; set; } = BottomPanelMode.TextInput;

    /// <summary>Gets or sets the prompt string for text input mode.</summary>
    public string Prompt { get; set; } = "You: ";

    /// <summary>Gets or sets the placeholder text shown when the input is empty.</summary>
    public string Placeholder { get; set; } = "";

    /// <summary>Gets or sets the highlight color for the active list item.</summary>
    public ConsoleColor ListHighlightColor { get; set; } = ConsoleColor.Cyan;

    /// <summary>Gets or sets the placeholder text for the custom text input option in the list.</summary>
    public string? ListCustomTextPlaceholder { get; set; }

    /// <summary>Gets or sets the foreground color for the rule borders and mode label.</summary>
    public ConsoleColor? ModeColor { get; set; }

    /// <summary>Gets or sets the current mode name displayed below the bottom rule (e.g. "plan").</summary>
    public string? ModeText { get; set; }

    /// <summary>Gets or sets the help text displayed below the bottom rule (available commands).</summary>
    public string? HelpText { get; set; }

    /// <summary>Gets or sets the title text displayed above the list selection (for interactive prompts).</summary>
    public string? ListTitle { get; set; }

    /// <summary>Gets or sets a value indicating whether input is enabled during streaming.</summary>
    public bool InputEnabled { get; set; }

    /// <summary>Gets or sets the prompt to show during streaming when input is disabled.</summary>
    public string StreamingPrompt { get; set; } = "(agent is running...)";

    /// <summary>Gets or sets a value indicating whether the agent status spinner is visible.</summary>
    public bool ShowSpinner { get; set; }

    /// <summary>Gets or sets the formatted token usage text to display in the status bar.</summary>
    public string? UsageText { get; set; }

    /// <summary>Gets or sets the queued input items to display above the rule.</summary>
    public IReadOnlyList<object> QueuedItems { get; set; } = [];
}

/// <summary>
/// Internal state for <see cref="HarnessAppComponent"/>.
/// </summary>
public record HarnessAppComponentState
{
    /// <summary>Gets the selected index in list selection mode.</summary>
    public int SelectedIndex { get; init; }

    /// <summary>Gets the current input text being typed.</summary>
    public string InputText { get; init; } = "";

    /// <summary>Gets the current text being typed into the list's custom text option.</summary>
    public string ListInputText { get; init; } = "";
}

/// <summary>
/// The main application component for the Harness console. Manages the scroll region
/// and bottom panel (text input, list selection, or streaming indicator), and emits
/// an <see cref="InputSubmitted"/> event when the user submits text in any mode.
/// </summary>
public class HarnessAppComponent : ConsoleReactiveComponent<HarnessAppComponentProps, HarnessAppComponentState>, IDisposable
{
    private readonly TopBottomRule _rule = new();
    private readonly ListSelection _listSelection = new();
    private readonly TextInput _textInput = new();
    private readonly TextScrollPanel _textScrollPanel;
    private readonly TextPanel _textPanel;
    private readonly TextPanel _queuedPanel;
    private readonly AgentStatus _agentStatus = new();
    private readonly AgentModeAndHelp _modeAndHelp = new();
    private readonly Func<object, string> _renderItem;
    private bool _resizedSinceLastRender;

    /// <summary>
    /// Initializes a new instance of the <see cref="HarnessAppComponent"/> class.
    /// </summary>
    /// <param name="renderScrollItem">A delegate that renders a single output entry and returns the text to display.</param>
    public HarnessAppComponent(Func<object, string> renderScrollItem)
    {
        this._renderItem = renderScrollItem;
        this._textScrollPanel = new TextScrollPanel(renderScrollItem);
        this._textPanel = new TextPanel(renderScrollItem);
        this._queuedPanel = new TextPanel(renderScrollItem);
        this.State = new HarnessAppComponentState();
        KeyEventListener.Instance.KeyPressed += this.OnKeyPressed;
        ConsoleResizeListener.Instance.ConsoleResized += this.OnConsoleResized;
    }

    /// <summary>
    /// Gets the 1-based row number of the last row in the output scroll region.
    /// </summary>
    public int ScrollRegionBottom { get; private set; }

    /// <summary>
    /// Occurs when the user submits input via Enter, in any mode (text input, list selection,
    /// or streaming injection). Consumers inspect <see cref="InputSubmittedEventArgs.Mode"/>
    /// to decide how to handle the submission.
    /// </summary>
    public event EventHandler<InputSubmittedEventArgs>? InputSubmitted;

    /// <summary>
    /// Deactivates the component, resetting the scroll region and unsubscribing from events.
    /// </summary>
    public void Deactivate()
    {
        this._agentStatus.Dispose();
        KeyEventListener.Instance.KeyPressed -= this.OnKeyPressed;
        ConsoleResizeListener.Instance.ConsoleResized -= this.OnConsoleResized;
        System.Console.Write(AnsiEscapes.ResetScrollRegion);
        System.Console.Write(AnsiEscapes.MoveCursor(System.Console.WindowHeight, 1));
        System.Console.WriteLine();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this._agentStatus.Dispose();
        }
    }

    private void OnKeyPressed(object? sender, KeyPressEventArgs e)
    {
        if (this.Props!.Mode == BottomPanelMode.TextInput)
        {
            this.HandleTextInputKey(e);
        }
        else if (this.Props.Mode == BottomPanelMode.ListSelection)
        {
            this.HandleListSelectionKey(e);
        }
        else if (this.Props.Mode == BottomPanelMode.Streaming && this.Props.InputEnabled)
        {
            this.HandleStreamingInputKey(e);
        }
    }

    private void HandleTextInputKey(KeyPressEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            string text = this.State!.InputText;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            this.SetState(this.State with { InputText = "" });
            this.InputSubmitted?.Invoke(this, new InputSubmittedEventArgs(text, BottomPanelMode.TextInput));
        }
        else if (e.KeyInfo.Key == ConsoleKey.Backspace)
        {
            if (this.State!.InputText.Length > 0)
            {
                this.SetState(this.State with { InputText = this.State.InputText[..^1] });
            }
        }
        else if (e.KeyInfo.KeyChar != '\0' && !char.IsControl(e.KeyInfo.KeyChar))
        {
            this.SetState(this.State! with { InputText = this.State.InputText + e.KeyInfo.KeyChar });
        }
    }

    private void HandleListSelectionKey(KeyPressEventArgs e)
    {
        int maxIndex = this.Props!.Items.Count - 1;
        if (this.Props.ListCustomTextPlaceholder != null)
        {
            maxIndex = this.Props.Items.Count;
        }

        bool isOnCustomTextOption = this.Props.ListCustomTextPlaceholder != null
            && this.State!.SelectedIndex == this.Props.Items.Count;

        if (e.KeyInfo.Key == ConsoleKey.UpArrow)
        {
            this.SetState(this.State! with { SelectedIndex = Math.Max(0, this.State.SelectedIndex - 1) });
        }
        else if (e.KeyInfo.Key == ConsoleKey.DownArrow)
        {
            this.SetState(this.State! with { SelectedIndex = Math.Min(maxIndex, this.State.SelectedIndex + 1) });
        }
        else if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            string result = isOnCustomTextOption
                ? this.State!.ListInputText
                : this.Props.Items[this.State!.SelectedIndex];

            this.SetState(this.State with { ListInputText = "", SelectedIndex = 0 });
            this.InputSubmitted?.Invoke(this, new InputSubmittedEventArgs(result, BottomPanelMode.ListSelection));
        }
        else if (isOnCustomTextOption)
        {
            if (e.KeyInfo.Key == ConsoleKey.Backspace)
            {
                if (this.State!.ListInputText.Length > 0)
                {
                    this.SetState(this.State with { ListInputText = this.State.ListInputText[..^1] });
                }
            }
            else if (e.KeyInfo.KeyChar != '\0' && !char.IsControl(e.KeyInfo.KeyChar))
            {
                this.SetState(this.State! with { ListInputText = this.State.ListInputText + e.KeyInfo.KeyChar });
            }
        }
    }

    private void HandleStreamingInputKey(KeyPressEventArgs e)
    {
        // During streaming with input enabled, capture text for message injection
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            string text = this.State!.InputText;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            this.SetState(this.State with { InputText = "" });
            this.InputSubmitted?.Invoke(this, new InputSubmittedEventArgs(text, BottomPanelMode.Streaming));
        }
        else if (e.KeyInfo.Key == ConsoleKey.Backspace)
        {
            if (this.State!.InputText.Length > 0)
            {
                this.SetState(this.State with { InputText = this.State.InputText[..^1] });
            }
        }
        else if (e.KeyInfo.KeyChar != '\0' && !char.IsControl(e.KeyInfo.KeyChar))
        {
            this.SetState(this.State! with { InputText = this.State.InputText + e.KeyInfo.KeyChar });
        }
    }

    private void OnConsoleResized(object? sender, ConsoleResizeEventArgs e)
    {
        this._resizedSinceLastRender = true;
        this.Render();
    }

    /// <inheritdoc />
    public override void RenderCore(HarnessAppComponentProps props, HarnessAppComponentState state)
    {
        // Determine the text panel height for the last scroll item
        IReadOnlyList<object> lastItems = props.ScrollItems.Count > 0
            ? [props.ScrollItems[^1]]
            : [];
        int textPanelHeight = TextPanel.CalculateHeight(lastItems, this._renderItem);
        if (textPanelHeight > 0)
        {
            textPanelHeight++; // Extra line for spacing between text panel and rule
        }

        // Calculate queued items panel height
        int queuedPanelHeight = TextPanel.CalculateHeight(props.QueuedItems, this._renderItem);

        // Build the bottom panel child based on mode
        ConsoleReactiveComponent bottomChild;
        int bottomChildHeight;

        if (props.Mode == BottomPanelMode.ListSelection)
        {
            var listProps = new ListSelectionProps
            {
                Title = props.ListTitle,
                Items = props.Items,
                SelectedIndex = state.SelectedIndex,
                HighlightColor = props.ListHighlightColor,
                CustomTextPlaceholder = props.ListCustomTextPlaceholder,
                CustomText = state.ListInputText,
            };

            bottomChildHeight = ListSelection.CalculateHeight(listProps);
            this._listSelection.Height = bottomChildHeight;
            this._listSelection.Props = listProps;
            bottomChild = this._listSelection;
        }
        else if (props.Mode == BottomPanelMode.Streaming)
        {
            TextInputProps textInputProps;
            if (props.InputEnabled)
            {
                textInputProps = new TextInputProps
                {
                    Prompt = props.Prompt,
                    Text = state.InputText,
                    Placeholder = props.Placeholder,
                };
            }
            else
            {
                textInputProps = new TextInputProps
                {
                    Prompt = props.Prompt,
                    Text = "",
                    Placeholder = props.StreamingPrompt,
                };
            }

            bottomChildHeight = TextInput.CalculateHeight(textInputProps, System.Console.WindowWidth);
            this._textInput.Width = System.Console.WindowWidth;
            this._textInput.Height = bottomChildHeight;
            this._textInput.Props = textInputProps;
            bottomChild = this._textInput;
        }
        else
        {
            var textInputProps = new TextInputProps
            {
                Prompt = props.Prompt,
                Text = state.InputText,
                Placeholder = props.Placeholder,
            };

            bottomChildHeight = TextInput.CalculateHeight(textInputProps, System.Console.WindowWidth);
            this._textInput.Width = System.Console.WindowWidth;
            this._textInput.Height = bottomChildHeight;
            this._textInput.Props = textInputProps;
            bottomChild = this._textInput;
        }

        var ruleProps = new TopBottomRuleProps
        {
            Width = System.Console.WindowWidth,
            Color = props.ModeColor,
            Children = [bottomChild],
        };

        // Calculate the agent status height
        var agentStatusProps = new AgentStatusProps
        {
            ShowSpinner = props.ShowSpinner,
            UsageText = props.UsageText,
        };
        int agentStatusHeight = AgentStatus.CalculateHeight(agentStatusProps);

        // Calculate the mode-and-help height
        var modeAndHelpProps = new AgentModeAndHelpProps
        {
            Mode = props.ModeText,
            ModeColor = props.ModeColor,
            HelpText = props.HelpText,
        };
        int modeAndHelpHeight = AgentModeAndHelp.CalculateHeight(modeAndHelpProps);

        int ruleHeight = TopBottomRule.CalculateHeight(ruleProps);
        int scrollBottom = System.Console.WindowHeight - ruleHeight - textPanelHeight - agentStatusHeight - queuedPanelHeight - modeAndHelpHeight;

        // If scroll region changed or a clear is needed, reset everything
        if (this._resizedSinceLastRender || (this.ScrollRegionBottom != 0 && scrollBottom != this.ScrollRegionBottom))
        {
            System.Console.Write(AnsiEscapes.EraseEntireScreen);
            System.Console.Write(AnsiEscapes.EraseScrollbackBuffer);
            this._textScrollPanel.Reset();
            this._resizedSinceLastRender = false;
        }

        this.ScrollRegionBottom = scrollBottom;

        System.Console.Write(AnsiEscapes.SetScrollRegion(scrollBottom));

        // Render text scroll panel in the scroll area (all items except the last)
        IReadOnlyList<object> scrollItems = props.ScrollItems.Count > 1
            ? props.ScrollItems.Take(props.ScrollItems.Count - 1).ToList()
            : [];

        this._textScrollPanel.X = 1;
        this._textScrollPanel.Y = 1;
        this._textScrollPanel.Width = System.Console.WindowWidth;
        this._textScrollPanel.Height = scrollBottom;
        this._textScrollPanel.Props = new TextScrollPanelProps
        {
            Items = scrollItems,
        };
        this._textScrollPanel.Render();

        // Render the text panel for the last (dynamic) item just below the scroll region
        this._textPanel.X = 1;
        this._textPanel.Y = scrollBottom + 1;
        this._textPanel.Width = System.Console.WindowWidth;
        this._textPanel.Height = textPanelHeight;
        this._textPanel.Props = new TextPanelProps
        {
            Items = lastItems,
        };
        this._textPanel.Render();

        // Render queued input items between text panel and agent status
        int queuedPanelY = scrollBottom + textPanelHeight + 1;
        this._queuedPanel.X = 1;
        this._queuedPanel.Y = queuedPanelY;
        this._queuedPanel.Width = System.Console.WindowWidth;
        this._queuedPanel.Height = queuedPanelHeight;
        this._queuedPanel.Props = new TextPanelProps
        {
            Items = props.QueuedItems,
        };
        this._queuedPanel.Render();

        // Render the agent status line between queued items and rule
        int agentStatusY = queuedPanelY + queuedPanelHeight;
        this._agentStatus.X = 1;
        this._agentStatus.Y = agentStatusY;
        this._agentStatus.Width = System.Console.WindowWidth;
        this._agentStatus.Height = agentStatusHeight;
        this._agentStatus.Props = agentStatusProps;
        this._agentStatus.Render();

        // Render the bottom rule + child below the agent status
        this._rule.X = 1;
        this._rule.Y = agentStatusY + agentStatusHeight;
        this._rule.Props = ruleProps;
        this._rule.Render();

        // Render the mode-and-help line below the bottom rule
        int modeAndHelpY = this._rule.Y + ruleHeight;
        this._modeAndHelp.X = 1;
        this._modeAndHelp.Y = modeAndHelpY;
        this._modeAndHelp.Width = System.Console.WindowWidth;
        this._modeAndHelp.Height = modeAndHelpHeight;
        this._modeAndHelp.Props = modeAndHelpProps;
        this._modeAndHelp.Render();

        // Position cursor for natural typing appearance
        this.PositionCursor(props, state);
    }

    private void PositionCursor(HarnessAppComponentProps props, HarnessAppComponentState state)
    {
        if (props.Mode == BottomPanelMode.TextInput
            || (props.Mode == BottomPanelMode.Streaming && props.InputEnabled))
        {
            int promptLength = props.Prompt.Length;
            int textWidth = System.Console.WindowWidth - promptLength;
            int textLength = state.InputText.Length;

            int textInputY = this._rule.Y + 1;

            if (textWidth <= 0 || textLength == 0)
            {
                System.Console.Write(AnsiEscapes.MoveCursor(textInputY, promptLength + 1));
            }
            else
            {
                int cursorRow = textLength < textWidth ? 0 : 1 + ((textLength - textWidth) / textWidth);
                int cursorCol = textLength < textWidth ? textLength : (textLength - textWidth) % textWidth;
                System.Console.Write(AnsiEscapes.MoveCursor(textInputY + cursorRow, promptLength + cursorCol + 1));
            }
        }
        else if (props.Mode == BottomPanelMode.ListSelection
            && props.ListCustomTextPlaceholder != null
            && state.SelectedIndex == props.Items.Count)
        {
            int titleLines = props.ListTitle?.Split('\n').Length ?? 0;
            int customOptionY = this._rule.Y + 1 + titleLines + props.Items.Count;
            int cursorCol = 2 + state.ListInputText.Length + 1;
            System.Console.Write(AnsiEscapes.MoveCursor(customOptionY, cursorCol));
        }
    }
}
