# Copyright (c) Microsoft. All rights reserved.

"""Tool approval observer for user confirmation of tool calls."""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

from ..app_state import ChoiceFollowUpQuestion, FollowUpAction
from .base import ConsoleObserver

if TYPE_CHECKING:
    from agent_framework import Agent, Content, Message

    from ..state_driver import IUXStateDriver


class ToolApprovalObserver(ConsoleObserver):
    """Asks user to approve tool calls before execution.

    Collects all tool calls during streaming and asks the user to approve
    or reject them after the stream completes. This provides an opportunity
    to review what the agent wants to do before it takes action.
    """

    def __init__(self) -> None:
        """Initialize the tool approval observer."""
        self._pending_tool_calls: list[Content] = []

    async def on_content(
        self,
        ux: IUXStateDriver,
        content: Content,
        agent: Agent,
        session: Any,
    ) -> None:
        """Collect tool calls for approval.

        Args:
            ux: The UX state driver for UI updates.
            content: The content item to check for function calls.
            agent: The AI agent.
            session: The agent session.
        """
        # Collect function call content for approval
        if content.type == "function_call":
            self._pending_tool_calls.append(content)

    async def on_stream_complete(
        self,
        ux: IUXStateDriver,
        agent: Agent,
        session: Any,
    ) -> list[FollowUpAction] | None:
        """Ask user to approve collected tool calls.

        Args:
            ux: The UX state driver for UI updates.
            agent: The AI agent.
            session: The agent session.

        Returns:
            List containing a ChoiceFollowUpQuestion for approval, or None if
            no tools were called.
        """
        if not self._pending_tool_calls:
            return None

        # Build list of tool names for display
        tool_names = [
            call.name if hasattr(call, "name") else str(call) for call in self._pending_tool_calls
        ]
        call_count = len(tool_names)

        # Create approval prompt
        if call_count == 1:
            prompt = f"Approve tool call: {tool_names[0]}?"
        else:
            prompt = f"Approve {call_count} tool calls: {', '.join(tool_names)}?"

        # Create choice question with approval/rejection options
        question = ChoiceFollowUpQuestion(
            prompt=prompt,
            choices=["Approve", "Reject"],
            allow_custom_text=False,
            continuation=self._handle_approval,
        )

        # Clear pending tools (they're now in the question's closure)
        self._pending_tool_calls.clear()

        return [question]

    async def _handle_approval(
        self,
        answer: str,
        ux: IUXStateDriver,
    ) -> Message | None:
        """Handle approval response from the user.

        Args:
            answer: The user's choice ("Approve" or "Reject").
            ux: The UX state driver for UI updates.

        Returns:
            Optional message to inject into the next agent turn. Returns None
            for approval (let execution continue), or a message for rejection
            (ask agent to try a different approach).
        """
        if answer == "Approve":
            ux.append_info_line("✓ Tools approved", "green")
            return None  # Continue with tool execution
        ux.append_info_line("✗ Tools rejected", "red")
        # Return a message asking the agent to try a different approach
        from agent_framework import Message

        return Message(
            role="user",
            contents=["The user rejected the tool calls. Please try a different approach without using tools."],
        )
