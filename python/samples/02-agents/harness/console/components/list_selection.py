# Copyright (c) Microsoft. All rights reserved.

"""List selection widget with optional custom text input."""

from __future__ import annotations

from textual import on
from textual.app import ComposeResult
from textual.binding import Binding
from textual.containers import Container
from textual.message import Message
from textual.reactive import reactive
from textual.widget import Widget
from textual.widgets import Input, Label, OptionList
from textual.widgets.option_list import Option


class HarnessListSelection(Widget):
    """List selection widget with numbered choices and optional custom text input.

    Displays a title, a list of numbered choices that can be selected via
    keyboard navigation or number keys (1-9), and an optional custom text
    input field at the bottom.

    Attributes:
        title: The title text displayed above the options.
        options: List of option strings to display.
        allow_custom_text: Whether to show a custom text input field.
    """

    title: reactive[str] = reactive("")
    options: reactive[list[str]] = reactive([])
    allow_custom_text: reactive[bool] = reactive(False)

    BINDINGS = [
        Binding("1", "select_option(0)", "Select option 1", show=False),
        Binding("2", "select_option(1)", "Select option 2", show=False),
        Binding("3", "select_option(2)", "Select option 3", show=False),
        Binding("4", "select_option(3)", "Select option 4", show=False),
        Binding("5", "select_option(4)", "Select option 5", show=False),
        Binding("6", "select_option(5)", "Select option 6", show=False),
        Binding("7", "select_option(6)", "Select option 7", show=False),
        Binding("8", "select_option(7)", "Select option 8", show=False),
        Binding("9", "select_option(8)", "Select option 9", show=False),
    ]

    class Selected(Message):
        """Message sent when an option is selected.

        Attributes:
            value: The selected option text or custom text.
        """

        def __init__(self, value: str) -> None:
            """Initialize the Selected message.

            Args:
                value: The selected option text or custom text.
            """
            self.value = value
            super().__init__()

    def compose(self) -> ComposeResult:
        """Compose the title, option list, and optional custom input.

        Yields:
            Title label, option list, and optionally a custom text input field.
        """
        with Container(classes="list-selection-container"):
            if self.title:
                yield Label(self.title, classes="selection-title", id="selection-title")
            yield OptionList(id="option-list")
            if self.allow_custom_text:
                yield Input(
                    placeholder="Or type custom text...",
                    classes="custom-input",
                    id="custom-input",
                )

    def on_mount(self) -> None:
        """Populate the option list when the widget is mounted."""
        self._update_options()

    def watch_title(self, new_title: str) -> None:
        """Update the title label when the title attribute changes.

        Args:
            new_title: The new title text.
        """
        try:
            label = self.query_one("#selection-title", Label)
            label.update(new_title)
        except Exception:
            pass  # Title label might not exist yet

    def watch_options(self, new_options: list[str]) -> None:
        """Update the option list when the options attribute changes.

        Args:
            new_options: The new list of options.
        """
        import contextlib

        with contextlib.suppress(Exception):
            self._update_options()

    def watch_allow_custom_text(self, allow: bool) -> None:
        """Show/hide the custom input field when allow_custom_text changes.

        Args:
            allow: Whether to allow custom text input.
        """
        try:
            custom_input = self.query_one("#custom-input", Input)
            custom_input.display = allow
        except Exception:
            pass  # Custom input might not exist yet

    def _update_options(self) -> None:
        """Update the OptionList with numbered options."""
        try:
            option_list = self.query_one("#option-list", OptionList)
            option_list.clear_options()

            for i, option_text in enumerate(self.options):
                # Add numbered prefix (1-9)
                display_text = f"{i + 1}. {option_text}" if i < 9 else f"   {option_text}"

                option_list.add_option(Option(display_text, id=str(i)))
        except Exception:
            pass  # OptionList might not exist yet

    @on(OptionList.OptionSelected)
    def on_option_selected(self, event: OptionList.OptionSelected) -> None:
        """Handle option selection from the list.

        Args:
            event: The OptionList.OptionSelected event.
        """
        option_index = int(event.option.id or "0")
        if 0 <= option_index < len(self.options):
            selected_value = self.options[option_index]
            self.post_message(self.Selected(selected_value))

    @on(Input.Submitted)
    def on_input_submitted(self, event: Input.Submitted) -> None:
        """Handle custom text input submission.

        Args:
            event: The Input.Submitted event.
        """
        if self.allow_custom_text and event.value:
            self.post_message(self.Selected(event.value))
            event.input.clear()

    def action_select_option(self, index: int) -> None:
        """Select an option by index (0-based).

        Args:
            index: The option index to select.
        """
        if 0 <= index < len(self.options):
            selected_value = self.options[index]
            self.post_message(self.Selected(selected_value))

    def focus_list(self) -> None:
        """Focus the option list."""
        option_list = self.query_one("#option-list", OptionList)
        option_list.focus()

    def focus_custom_input(self) -> None:
        """Focus the custom text input field."""
        if self.allow_custom_text:
            custom_input = self.query_one("#custom-input", Input)
            custom_input.focus()
