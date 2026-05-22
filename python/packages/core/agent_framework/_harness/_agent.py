# Copyright (c) Microsoft. All rights reserved.

"""HarnessAgent: a pre-configured bundled agent with batteries included.

This module provides :class:`HarnessAgent`, a convenience class that assembles
the full agent pipeline from a chat client, wiring up function invocation,
per-service-call history persistence, compaction, and a rich set of default
context providers (todo, mode, memory, skills).
"""

from __future__ import annotations

from collections.abc import Awaitable, Callable, Mapping, Sequence
from typing import TYPE_CHECKING, Any, Literal, overload

from .._agents import AgentSession, BaseAgent
from .._compaction import CompactionProvider, ContextWindowCompactionStrategy, ToolResultCompactionStrategy
from .._feature_stage import ExperimentalFeature, experimental
from .._sessions import ContextProvider, HistoryProvider, InMemoryHistoryProvider
from .._skills import SkillsProvider
from .._types import AgentResponse, AgentResponseUpdate, AgentRunInputs, ResponseStream
from ._memory import MemoryContextProvider, MemoryStore
from ._mode import AgentModeProvider
from ._todo import TodoProvider

if TYPE_CHECKING:
    from .._clients import SupportsChatGetResponse
    from .._compaction import CompactionStrategy, TokenizerProtocol
    from .._middleware import MiddlewareTypes
    from .._tools import ToolTypes

DEFAULT_HARNESS_INSTRUCTIONS = """\
You are a helpful AI assistant that uses tools to complete tasks.

## General guidelines

- Think through the task before acting. Break complex work into clear steps.
- Use the tools available to you to gather information, perform actions, and verify results.
- Explain your reasoning and thought process as you work through tasks.
- Explain what you learned and what you are going to do next between tool calls, \
so the user can follow along with your thought process.
- Avoid making more than 4 tool calls in a row without explaining what you are doing.
- If a tool call fails or returns unexpected results, adapt your approach rather than \
repeating the same call.
- When you have completed the task, present a clear and concise summary of what you did \
and what you found.
"""


def _assemble_instructions(
    harness_instructions: str | None,
    agent_instructions: str | None,
) -> str | None:
    """Assemble final instructions from harness + agent instructions."""
    harness = harness_instructions if harness_instructions is not None else DEFAULT_HARNESS_INSTRUCTIONS
    agent = agent_instructions

    if not harness and not agent:
        return DEFAULT_HARNESS_INSTRUCTIONS
    if not harness:
        return agent
    if not agent:
        return harness
    return f"{harness}\n\n{agent}"


def _assemble_compaction_provider(
    *,
    disable_compaction: bool,
    max_context_window_tokens: int,
    max_output_tokens: int,
    history_source_id: str,
    before_compaction_strategy: CompactionStrategy | None,
    after_compaction_strategy: CompactionStrategy | None,
    tokenizer: TokenizerProtocol | None,
) -> CompactionProvider | None:
    """Build the compaction provider from parameters or defaults."""
    if disable_compaction:
        return None

    before_strategy = before_compaction_strategy or ContextWindowCompactionStrategy(
        max_context_window_tokens=max_context_window_tokens,
        max_output_tokens=max_output_tokens,
        tokenizer=tokenizer,
    )
    after_strategy = after_compaction_strategy or ToolResultCompactionStrategy(keep_last_tool_call_groups=2)

    return CompactionProvider(
        before_strategy=before_strategy,
        after_strategy=after_strategy,
        tokenizer=tokenizer,
        history_source_id=history_source_id,
    )


def _assemble_context_providers(
    *,
    history_provider: HistoryProvider,
    compaction_provider: CompactionProvider | None,
    disable_todo: bool,
    todo_provider: TodoProvider | None,
    disable_mode: bool,
    mode_provider: AgentModeProvider | None,
    disable_memory: bool,
    memory_store: MemoryStore | None,
    disable_skills: bool,
    skills_provider: SkillsProvider | None,
    skills_paths: Sequence[str] | None,
    extra_context_providers: Sequence[ContextProvider] | None,
) -> list[ContextProvider]:
    """Assemble the ordered list of context providers."""
    providers: list[ContextProvider] = []

    # History first so other providers can access loaded messages.
    providers.append(history_provider)

    # Compaction runs after history loads messages.
    if compaction_provider is not None:
        providers.append(compaction_provider)

    if not disable_todo:
        providers.append(todo_provider or TodoProvider())

    if not disable_mode:
        providers.append(mode_provider or AgentModeProvider())

    if not disable_memory and memory_store is not None:
        providers.append(MemoryContextProvider(store=memory_store))

    if not disable_skills:
        skills: SkillsProvider | None = skills_provider
        if skills is None:
            skills = SkillsProvider.from_paths(*skills_paths) if skills_paths else SkillsProvider.from_paths(".")
        providers.append(skills)

    # Append any user-supplied additional providers.
    if extra_context_providers:
        providers.extend(extra_context_providers)

    return providers


@experimental(feature_id=ExperimentalFeature.HARNESS)
class HarnessAgent(BaseAgent):
    """A pre-configured agent that bundles function invocation, history persistence, compaction, and context providers.

    ``HarnessAgent`` assembles an :class:`~agent_framework.Agent` pipeline from a
    caller-supplied chat client, automatically wiring:

    - **Function invocation** — automatic tool calling loop
    - **Per-service-call history persistence** — persists history after every model call
    - **Compaction** — context-window compaction before/after each run
    - **TodoProvider** — todo list management
    - **AgentModeProvider** — plan/execute mode tracking
    - **MemoryContextProvider** — file-based durable memory (when ``memory_store`` provided)
    - **SkillsProvider** — skill discovery and progressive loading
    - **OpenTelemetry** — observability via ``AgentTelemetryLayer``

    Each feature can be disabled or customized via keyword arguments.

    Examples:
        Basic usage:

        .. code-block:: python

            from agent_framework import HarnessAgent
            from agent_framework.openai import OpenAIChatClient

            agent = HarnessAgent(
                client=OpenAIChatClient(model="gpt-4o"),
                max_context_window_tokens=128_000,
                max_output_tokens=16_384,
            )
            session = agent.create_session()
            response = await agent.run("Plan a weekend trip to Seattle", session=session)

        With customization:

        .. code-block:: python

            agent = HarnessAgent(
                client=client,
                max_context_window_tokens=200_000,
                max_output_tokens=32_000,
                name="research-agent",
                agent_instructions="Focus on academic sources.",
                disable_todo=True,
                skills_paths=["./skills", "./custom-skills"],
            )
    """

    AGENT_PROVIDER_NAME = "microsoft.agent_framework.harness"

    def __init__(
        self,
        client: SupportsChatGetResponse[Any],
        max_context_window_tokens: int,
        max_output_tokens: int,
        *,
        id: str | None = None,
        name: str | None = None,
        description: str | None = None,
        harness_instructions: str | None = None,
        agent_instructions: str | None = None,
        tools: ToolTypes | Callable[..., Any] | Sequence[ToolTypes | Callable[..., Any]] | None = None,
        history_provider: HistoryProvider | None = None,
        disable_compaction: bool = False,
        before_compaction_strategy: CompactionStrategy | None = None,
        after_compaction_strategy: CompactionStrategy | None = None,
        tokenizer: TokenizerProtocol | None = None,
        disable_todo: bool = False,
        todo_provider: TodoProvider | None = None,
        disable_mode: bool = False,
        mode_provider: AgentModeProvider | None = None,
        disable_memory: bool = False,
        memory_store: MemoryStore | None = None,
        disable_skills: bool = False,
        skills_provider: SkillsProvider | None = None,
        skills_paths: Sequence[str] | None = None,
        disable_telemetry: bool = False,
        otel_provider_name: str | None = None,
        context_providers: Sequence[ContextProvider] | None = None,
        middleware: Sequence[MiddlewareTypes] | None = None,
        default_options: Mapping[str, Any] | None = None,
    ) -> None:
        """Initialize a HarnessAgent.

        Args:
            client: The chat client providing access to the underlying AI model.
            max_context_window_tokens: Maximum tokens the model's context window supports.
            max_output_tokens: Maximum output tokens per response.

        Keyword Args:
            id: Optional agent ID (auto-generated UUID if omitted).
            name: Optional agent name.
            description: Optional agent description.
            harness_instructions: Override the default harness-level instructions.
                Set to empty string to omit harness instructions entirely.
            agent_instructions: Agent-specific instructions appended after harness instructions.
            tools: Additional tools to include in the agent's toolset.
            history_provider: Custom history provider. When None, an InMemoryHistoryProvider is used.
            disable_compaction: When True, skip compaction provider setup.
            before_compaction_strategy: Custom before-run compaction strategy.
                Defaults to ContextWindowCompactionStrategy (token-budget aware).
            after_compaction_strategy: Custom after-run compaction strategy.
                Defaults to ToolResultCompactionStrategy.
            tokenizer: Custom tokenizer for compaction strategies.
            disable_todo: When True, skip the TodoProvider.
            todo_provider: Custom TodoProvider instance. Ignored when disable_todo is True.
            disable_mode: When True, skip the AgentModeProvider.
            mode_provider: Custom AgentModeProvider instance. Ignored when disable_mode is True.
            disable_memory: When True, skip the MemoryContextProvider.
            memory_store: Memory store instance. When provided (and disable_memory is False),
                a MemoryContextProvider is added.
            disable_skills: When True, skip the SkillsProvider.
            skills_provider: Custom SkillsProvider instance. Ignored when disable_skills is True.
            skills_paths: Paths for file-based skill discovery.
                Ignored when skills_provider is set or disable_skills is True.
            disable_telemetry: When True, use RawAgent (no telemetry layer) instead of Agent.
            otel_provider_name: Custom OpenTelemetry provider/source name.
            context_providers: Additional context providers to include after the built-in ones.
            middleware: Additional middleware to include.
            default_options: Provider-specific chat options (temperature, max_tokens, etc.).

        Raises:
            ValueError: If max_context_window_tokens <= 0 or max_output_tokens < 0
                or max_output_tokens >= max_context_window_tokens.
        """
        if max_context_window_tokens <= 0:
            raise ValueError("max_context_window_tokens must be positive.")
        if max_output_tokens < 0:
            raise ValueError("max_output_tokens must be non-negative.")
        if max_output_tokens >= max_context_window_tokens:
            raise ValueError("max_output_tokens must be less than max_context_window_tokens.")

        super().__init__(
            id=id,
            name=name,
            description=description,
        )

        # Build history provider.
        resolved_history = history_provider or InMemoryHistoryProvider()

        # Build compaction provider.
        compaction_provider = _assemble_compaction_provider(
            disable_compaction=disable_compaction,
            max_context_window_tokens=max_context_window_tokens,
            max_output_tokens=max_output_tokens,
            history_source_id=resolved_history.source_id,
            before_compaction_strategy=before_compaction_strategy,
            after_compaction_strategy=after_compaction_strategy,
            tokenizer=tokenizer,
        )

        # Build context providers.
        assembled_providers = _assemble_context_providers(
            history_provider=resolved_history,
            compaction_provider=compaction_provider,
            disable_todo=disable_todo,
            todo_provider=todo_provider,
            disable_mode=disable_mode,
            mode_provider=mode_provider,
            disable_memory=disable_memory,
            memory_store=memory_store,
            disable_skills=disable_skills,
            skills_provider=skills_provider,
            skills_paths=skills_paths,
            extra_context_providers=context_providers,
        )

        # Build instructions.
        instructions = _assemble_instructions(harness_instructions, agent_instructions)

        # Build default options dict.
        default_opts: dict[str, Any] = dict(default_options) if default_options else {}
        default_opts.setdefault("max_tokens", max_output_tokens)

        # Determine agent class based on telemetry preference.
        from .._agents import Agent as FullAgent
        from .._agents import RawAgent

        agent_cls: type[RawAgent[Any]] = FullAgent if not disable_telemetry else RawAgent

        # Build additional kwargs for telemetry.
        agent_kwargs: dict[str, Any] = {}
        if agent_cls is FullAgent and otel_provider_name:
            agent_kwargs["otel_agent_provider_name"] = otel_provider_name

        # Build the inner agent.
        self._inner_agent = agent_cls(
            client,
            instructions,
            id=self.id,
            name=self.name,
            description=self.description,
            tools=tools,
            default_options=default_opts,  # type: ignore[arg-type]
            context_providers=assembled_providers,
            middleware=list(middleware) if middleware else None,
            require_per_service_call_history_persistence=True,
            **agent_kwargs,
        )

        # Store for introspection.
        self.max_context_window_tokens = max_context_window_tokens
        self.max_output_tokens = max_output_tokens
        self.context_providers = self._inner_agent.context_providers

    @overload
    def run(
        self,
        messages: AgentRunInputs | None = None,
        *,
        stream: Literal[False] = ...,
        session: AgentSession | None = None,
        function_invocation_kwargs: Mapping[str, Any] | None = None,
        client_kwargs: Mapping[str, Any] | None = None,
    ) -> Awaitable[AgentResponse[Any]]: ...

    @overload
    def run(
        self,
        messages: AgentRunInputs | None = None,
        *,
        stream: Literal[True],
        session: AgentSession | None = None,
        function_invocation_kwargs: Mapping[str, Any] | None = None,
        client_kwargs: Mapping[str, Any] | None = None,
    ) -> ResponseStream[AgentResponseUpdate, AgentResponse[Any]]: ...

    def run(
        self,
        messages: AgentRunInputs | None = None,
        *,
        stream: bool = False,
        session: AgentSession | None = None,
        function_invocation_kwargs: Mapping[str, Any] | None = None,
        client_kwargs: Mapping[str, Any] | None = None,
    ) -> Awaitable[AgentResponse[Any]] | ResponseStream[AgentResponseUpdate, AgentResponse[Any]]:
        """Run the harness agent.

        Delegates to the inner agent, which includes function invocation,
        per-service-call persistence, compaction, and all configured providers.

        Args:
            messages: The message(s) to send to the agent.

        Keyword Args:
            stream: Whether to stream the response.
            session: The conversation session.
            function_invocation_kwargs: Keyword arguments forwarded to tool invocation.
            client_kwargs: Additional client-specific keyword arguments.

        Returns:
            When stream=False: An awaitable AgentResponse.
            When stream=True: A ResponseStream of AgentResponseUpdate items.
        """
        return self._inner_agent.run(
            messages,
            stream=stream,  # type: ignore[arg-type]
            session=session,
            function_invocation_kwargs=function_invocation_kwargs,
            client_kwargs=client_kwargs,
        )

    def create_session(self, *, session_id: str | None = None) -> AgentSession:
        """Create a new conversation session.

        Keyword Args:
            session_id: Optional session ID (generated if not provided).

        Returns:
            A new AgentSession instance.
        """
        return self._inner_agent.create_session(session_id=session_id)

    def get_session(self, service_session_id: str, *, session_id: str | None = None) -> AgentSession:
        """Get a session for a service-managed session ID.

        Args:
            service_session_id: The service-managed session ID.

        Keyword Args:
            session_id: Optional local session ID.

        Returns:
            An AgentSession instance with the service_session_id set.
        """
        return self._inner_agent.get_session(service_session_id, session_id=session_id)
