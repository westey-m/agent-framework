# Copyright (c) Microsoft. All rights reserved.

"""Console observers for agent streaming lifecycle.

This module provides observers that display events during agent streaming
and collect follow-up actions. All observers use the IUXStateDriver interface
to update the UI.
"""

from .base import ConsoleObserver
from .error_display import ErrorDisplayObserver
from .reasoning_display import ReasoningDisplayObserver
from .text_output import TextOutputObserver
from .tool_approval import ToolApprovalObserver
from .tool_call_display import ToolCallDisplayObserver
from .usage_display import UsageDisplayObserver


def build_default_observers() -> list[ConsoleObserver]:
    """Build the default set of observers for the harness console.

    Returns a standard observer list covering:
    - Text output (streaming text display)
    - Tool call display (formatted tool invocations)
    - Error display (error messages)
    - Usage display (token counts)
    - Reasoning display (reasoning/thinking blocks)
    - Tool approval (user approval for tool calls)

    Returns:
        List of default console observers.
    """
    return [
        TextOutputObserver(),
        ToolCallDisplayObserver(),
        ErrorDisplayObserver(),
        UsageDisplayObserver(),
        ReasoningDisplayObserver(),
        ToolApprovalObserver(),
    ]


__all__ = [
    "ConsoleObserver",
    "ErrorDisplayObserver",
    "ReasoningDisplayObserver",
    "TextOutputObserver",
    "ToolApprovalObserver",
    "ToolCallDisplayObserver",
    "UsageDisplayObserver",
    "build_default_observers",
]
