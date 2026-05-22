# Copyright (c) Microsoft. All rights reserved.

from __future__ import annotations

from collections.abc import AsyncIterator, Mapping
from typing import Any

import pytest

from agent_framework import (
    AgentSession,
    ChatResponse,
    CompactionProvider,
    HarnessAgent,
    InMemoryHistoryProvider,
    Message,
    SkillsProvider,
    TodoProvider,
)
from agent_framework._harness._agent import DEFAULT_HARNESS_INSTRUCTIONS, _assemble_instructions
from agent_framework._harness._mode import AgentModeProvider
from agent_framework._sessions import ContextProvider


class _FakeChatClient:
    """Minimal chat client stub for testing assembly."""

    model = "test-model"

    async def get_response(
        self,
        *,
        messages: list[Message],
        options: Mapping[str, Any] | None = None,
        **kwargs: Any,
    ) -> ChatResponse:
        return ChatResponse(messages=[Message(role="assistant", contents=["Hello"])])

    async def get_streaming_response(
        self,
        *,
        messages: list[Message],
        options: Mapping[str, Any] | None = None,
        **kwargs: Any,
    ) -> AsyncIterator[Any]:
        yield Message(role="assistant", contents=["Hello"])  # pragma: no cover


# --- Assembly Tests ---


def test_harness_agent_creates_with_defaults() -> None:
    """HarnessAgent should assemble successfully with default options."""
    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
    )
    assert agent.id is not None
    assert agent._inner_agent is not None


def test_harness_agent_includes_all_default_providers() -> None:
    """Default assembly should include history, compaction, todo, mode, skills."""
    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
    )
    providers = agent.context_providers
    provider_types = [type(p) for p in providers]

    assert InMemoryHistoryProvider in provider_types
    assert CompactionProvider in provider_types
    assert TodoProvider in provider_types
    assert AgentModeProvider in provider_types
    assert SkillsProvider in provider_types


def test_harness_agent_disable_todo() -> None:
    """disable_todo=True should exclude TodoProvider."""
    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
        disable_todo=True,
    )
    provider_types = [type(p) for p in agent.context_providers]
    assert TodoProvider not in provider_types


def test_harness_agent_disable_mode() -> None:
    """disable_mode=True should exclude AgentModeProvider."""
    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
        disable_mode=True,
    )
    provider_types = [type(p) for p in agent.context_providers]
    assert AgentModeProvider not in provider_types


def test_harness_agent_disable_memory() -> None:
    """disable_memory=True should exclude MemoryContextProvider."""
    from agent_framework import MemoryContextProvider

    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
        disable_memory=True,
    )
    provider_types = [type(p) for p in agent.context_providers]
    assert MemoryContextProvider not in provider_types


def test_harness_agent_disable_skills() -> None:
    """disable_skills=True should exclude SkillsProvider."""
    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
        disable_skills=True,
    )
    provider_types = [type(p) for p in agent.context_providers]
    assert SkillsProvider not in provider_types


def test_harness_agent_disable_compaction() -> None:
    """disable_compaction=True should exclude CompactionProvider."""
    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
        disable_compaction=True,
    )
    provider_types = [type(p) for p in agent.context_providers]
    assert CompactionProvider not in provider_types


def test_harness_agent_disable_telemetry_uses_raw_agent() -> None:
    """disable_telemetry=True should use RawAgent instead of Agent."""
    from agent_framework._agents import Agent as FullAgent
    from agent_framework._agents import RawAgent

    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
        disable_telemetry=True,
    )
    assert isinstance(agent._inner_agent, RawAgent)
    assert not isinstance(agent._inner_agent, FullAgent)


def test_harness_agent_default_uses_full_agent() -> None:
    """Default assembly should use Agent (with telemetry)."""
    from agent_framework._agents import Agent as FullAgent

    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
    )
    assert isinstance(agent._inner_agent, FullAgent)


# --- Validation Tests ---


def test_harness_agent_rejects_invalid_context_tokens() -> None:
    """max_context_window_tokens must be positive."""
    with pytest.raises(ValueError, match="max_context_window_tokens must be positive"):
        HarnessAgent(
            client=_FakeChatClient(),  # type: ignore[arg-type]
            max_context_window_tokens=0,
            max_output_tokens=100,
        )


def test_harness_agent_rejects_negative_output_tokens() -> None:
    """max_output_tokens must be non-negative."""
    with pytest.raises(ValueError, match="max_output_tokens must be non-negative"):
        HarnessAgent(
            client=_FakeChatClient(),  # type: ignore[arg-type]
            max_context_window_tokens=1000,
            max_output_tokens=-1,
        )


def test_harness_agent_rejects_output_gte_context() -> None:
    """max_output_tokens must be less than max_context_window_tokens."""
    with pytest.raises(ValueError, match="max_output_tokens must be less than"):
        HarnessAgent(
            client=_FakeChatClient(),  # type: ignore[arg-type]
            max_context_window_tokens=1000,
            max_output_tokens=1000,
        )


# --- Instructions Tests ---


def test_default_instructions() -> None:
    """None args should produce default harness instructions."""
    result = _assemble_instructions(None, None)
    assert result == DEFAULT_HARNESS_INSTRUCTIONS


def test_custom_agent_instructions_appended() -> None:
    """Agent instructions should be appended after harness instructions."""
    result = _assemble_instructions(None, "Focus on code review.")
    assert DEFAULT_HARNESS_INSTRUCTIONS in result  # type: ignore[operator]
    assert "Focus on code review." in result  # type: ignore[operator]


def test_empty_harness_instructions_uses_agent_only() -> None:
    """Empty harness_instructions should return agent instructions only."""
    result = _assemble_instructions("", "Custom only.")
    assert result == "Custom only."


# --- Identity Tests ---


def test_harness_agent_custom_identity() -> None:
    """Custom id, name, description should propagate."""
    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
        id="my-agent-id",
        name="my-agent",
        description="A test agent",
    )
    assert agent.id == "my-agent-id"
    assert agent.name == "my-agent"
    assert agent.description == "A test agent"


# --- Session Tests ---


def test_harness_agent_create_session() -> None:
    """create_session should return an AgentSession."""
    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
    )
    session = agent.create_session()
    assert isinstance(session, AgentSession)


def test_harness_agent_create_session_with_id() -> None:
    """create_session should accept a custom session_id."""
    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
    )
    session = agent.create_session(session_id="custom-id")
    assert session.session_id == "custom-id"


# --- Protocol Tests ---


def test_harness_agent_satisfies_protocol() -> None:
    """HarnessAgent should satisfy SupportsAgentRun protocol."""
    from agent_framework import SupportsAgentRun

    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
    )
    assert isinstance(agent, SupportsAgentRun)


# --- Additional providers ---


def test_harness_agent_extra_context_providers() -> None:
    """Additional context_providers should be appended."""

    class _CustomProvider(ContextProvider):
        pass

    custom = _CustomProvider("custom")
    agent = HarnessAgent(
        client=_FakeChatClient(),  # type: ignore[arg-type]
        max_context_window_tokens=128_000,
        max_output_tokens=16_384,
        context_providers=[custom],
    )
    assert custom in agent.context_providers
