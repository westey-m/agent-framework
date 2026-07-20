# /// script
# requires-python = ">=3.10"
# dependencies = [
#     "agent-framework",
#     "agent-framework-tools",
#     "agent-framework-monty",
#     "mcp",
#     "httpx",
#     "azure-identity",
#     "python-dotenv",
# ]
# ///

# Copyright (c) Microsoft. All rights reserved.

"""Shared production-ready claw agent factory for Post 4.

Builds the same personal finance claw as Step 03 and adds production wiring for
observability-aware hosts plus opt-in Microsoft Purview chat policy middleware.

Environment variables:
    FOUNDRY_PROJECT_ENDPOINT       — Microsoft Foundry project endpoint URL
    FOUNDRY_MODEL                  — Model deployment name for local hosts (defaults to gpt-5.4)
    FOUNDRY_TOOLBOX_MCP_SERVER_URL — Optional Foundry Toolbox MCP endpoint URL for managed skills
    PURVIEW_CLIENT_APP_ID          — Optional app/client ID; enables Purview chat policy middleware

Run indirectly through a host, for example:
    uv run python/samples/02-agents/harness/build_your_own_claw/claw_step04_production_ready/console.py
"""

from __future__ import annotations

import os
import sys
import uuid
from collections.abc import Callable, Generator, Mapping
from contextlib import AsyncExitStack
from datetime import datetime, timezone
from pathlib import Path
from typing import Annotated, Any, Literal

import httpx
from agent_framework import (
    AggregatingSkillsSource,
    Agent,
    AgentModeProvider,
    DeduplicatingSkillsSource,
    FileAccessProvider,
    FileSkillsSource,
    FileSystemAgentFileStore,
    HistoryProvider,
    InMemoryHistoryProvider,
    MCPSkillsSource,
    SkillsProvider,
    SkillsSource,
    create_harness_agent,
    tool,
)
from agent_framework.foundry import FoundryChatClient
from agent_framework.microsoft import PurviewChatPolicyMiddleware, PurviewSettings
from agent_framework_monty import MontyCodeActProvider
from agent_framework_tools.shell import LocalShellTool, ShellPolicy
from azure.core.credentials import TokenCredential
from azure.identity import AzureCliCredential, InteractiveBrowserCredential, get_bearer_token_provider
from dotenv import load_dotenv
from mcp.client.session import ClientSession
from mcp.client.streamable_http import streamable_http_client
from pydantic import Field

# Resolve everything the hosted container needs from this folder so it is a self-contained Docker
# build context (see Dockerfile / README). ``subprocess_script_runner.py`` and ``skills/`` live
# beside this file; ``working/`` (used only by the local file-access and shell hosts) stays in the
# parent sample folder and is unused on the hosted container, where file access and shell are off.
_SELF_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(_SELF_DIR))
from subprocess_script_runner import subprocess_script_runner  # noqa: E402

_WORKING_DIR = _SELF_DIR.parent / "working"
_VAULT_DIR = _WORKING_DIR / "confirmations"
_SKILLS_DIR = _SELF_DIR / "skills"

FINANCE_INSTRUCTIONS = """\
## Personal Finance Assistant Instructions

You are a personal finance and investing assistant. You help the user understand their portfolio
and watchlist, value individual stocks, gauge portfolio risk, research the market, and keep their
records tidy.

### Working style

- The user's holdings live in a file called portfolio.csv. Read it with the file_access tools
  before answering questions about their portfolio, and never modify it unless asked.
- You have skills for valuation and risk-scoring. When a question matches a skill, load it and
  follow its instructions (read its references, run its scripts) rather than guessing.
- When asked to research several tickers, delegate each one to the background research agent so
  they run concurrently, then summarize the findings together.
- The user's trade confirmations accumulate in the working/confirmations folder. When asked to tidy
  or reorganize them, use the run_shell tool: inspect the folder first, then move files into a
  year/month layout and rename them to YYYY-MM-DD_TICKER_BUY|SELL.txt. Explain your plan before
  running commands that change anything.
- To buy or sell, use the place_trade tool. This takes a real action, so the user will be asked to
  approve it before it runs — explain what you are about to do first.

### Important

You provide information and analysis only — you are not a licensed financial advisor and you must
not present your output as personalized investment advice. Remind the user to do their own
research before making decisions.
"""

_PRICE_BOOK: dict[str, tuple[float, float]] = {
    "MSFT": (462.97, 11.80),
    "AAPL": (229.35, 6.13),
    "GOOGL": (178.12, 7.54),
    "AMZN": (201.45, 4.18),
    "NVDA": (134.81, 2.95),
    "SPY": (612.40, 23.10),
}


# <get_stock_price>
def get_stock_price(
    symbol: Annotated[str, "The stock ticker symbol, e.g. MSFT or AAPL."],
) -> dict[str, object]:
    """Get the latest delayed, illustrative stock price and trailing EPS for a ticker symbol."""
    ticker = symbol.upper()
    data = _PRICE_BOOK.get(ticker)
    if data is None:
        seed = 0
        for ch in ticker:
            seed = (seed * 31 + ord(ch)) % 1_000_000
        price = 50.0 + (seed % 45000) / 100.0
        data = (price, round(price / 20.0, 2))

    return {
        "symbol": ticker,
        "price": round(data[0], 2),
        "trailing_eps": round(data[1], 2),
        "currency": "USD",
        "as_of": datetime.now(timezone.utc).isoformat(),
    }
# </get_stock_price>


# <place_trade>
@tool(approval_mode="always_require")
def place_trade(
    symbol: Annotated[str, "The stock ticker symbol to trade, e.g. MSFT."],
    action: Annotated[Literal["buy", "sell"], "Either 'buy' or 'sell'."],
    quantity: Annotated[int, Field(gt=0, description="The number of shares to trade.")],
) -> str:
    """Place a simulated buy or sell order; no real order is placed."""
    verb = "Sold" if action == "sell" else "Bought"
    confirmation = f"TRADE-{uuid.uuid4().hex[:8].upper()}"
    return f"{verb} {quantity} share(s) of {symbol.upper()}. Confirmation: {confirmation}."
# </place_trade>


# <skills>
async def _build_skills_provider(stack: AsyncExitStack) -> SkillsProvider:
    """Build local file-based skills plus optional Foundry Toolbox MCP skills."""
    sources: list[SkillsSource] = [FileSkillsSource(str(_SKILLS_DIR), script_runner=subprocess_script_runner)]

    toolbox_url = os.environ.get("FOUNDRY_TOOLBOX_MCP_SERVER_URL")
    if toolbox_url:
        session = await _connect_foundry_toolbox(stack, toolbox_url)
        sources.append(MCPSkillsSource(client=session))
        print("Foundry skills enabled (Toolbox MCP).")
    else:
        print("Foundry skills disabled. Set FOUNDRY_TOOLBOX_MCP_SERVER_URL to enable them.")

    source: SkillsSource = sources[0] if len(sources) == 1 else AggregatingSkillsSource(sources)
    return SkillsProvider(DeduplicatingSkillsSource(source))


class _ToolboxAuth(httpx.Auth):
    """Attach a fresh Foundry bearer token to every request."""

    def __init__(self, token_provider: Callable[[], str]):
        """Initialize the auth helper with a token provider."""
        self._get_token = token_provider

    def auth_flow(self, request: httpx.Request) -> Generator[httpx.Request, httpx.Response, None]:
        """Attach an authorization header to the outgoing MCP request."""
        request.headers["Authorization"] = f"Bearer {self._get_token()}"
        yield request


async def _connect_foundry_toolbox(stack: AsyncExitStack, url: str) -> ClientSession:
    """Open an MCP session against a Foundry Toolbox endpoint tied to ``stack``'s lifetime."""
    token_provider = get_bearer_token_provider(AzureCliCredential(), "https://ai.azure.com/.default")
    http_client = await stack.enter_async_context(
        httpx.AsyncClient(
            auth=_ToolboxAuth(token_provider),
            headers={"Foundry-Features": "Toolboxes=V1Preview"},
            timeout=httpx.Timeout(30.0, read=300.0),
            follow_redirects=True,
        )
    )
    read, write, _ = await stack.enter_async_context(streamable_http_client(url=url, http_client=http_client))
    session = await stack.enter_async_context(ClientSession(read, write))
    await session.initialize()
    return session
# </skills>


# <background>
def _build_research_agent(client: FoundryChatClient) -> Any:
    """Build the lean web-search-only chat agent used for per-ticker research."""
    return Agent(
        client=client,
        name="TickerResearchAgent",
        description="Searches the web for recent news and commentary about a single stock ticker.",
        tools=[client.get_web_search_tool()],
        instructions=(
            "You research a single stock ticker. Use the web search tool to find the most recent, "
            "relevant news and commentary, then return a short, factual summary (3-4 bullet points) "
            "with no preamble."
        ),
    )
# </background>


# <shell>
def _build_shell() -> LocalShellTool:
    """Build a sandboxed shell confined to the trade-confirmation vault."""
    return LocalShellTool(
        mode="persistent",
        workdir=str(_VAULT_DIR),
        confine_workdir=True,
        policy=ShellPolicy(
            denylist=[
                r"\brm\s+-rf\b",
                r"\bsudo\b",
                r":\(\)\s*\{",
                r"\bmkfs\b",
                r">\s*/dev/sd",
            ],
        ),
        timeout=15,
    )
# </shell>


def _build_purview_middleware() -> list[PurviewChatPolicyMiddleware]:
    """Build opt-in Purview chat middleware from environment variables."""
    client_app_id = os.environ.get("PURVIEW_CLIENT_APP_ID")
    if not client_app_id:
        print("Purview disabled. Set PURVIEW_CLIENT_APP_ID to enable chat policy enforcement.")
        return []

    credential = InteractiveBrowserCredential(client_id=client_app_id)
    settings = PurviewSettings(app_name="Claw")
    print("Purview enabled (chat policy middleware).")
    return [PurviewChatPolicyMiddleware(credential, settings)]


# <build_claw_agent>
async def build_claw_agent(
    stack: AsyncExitStack,
    *,
    credential: TokenCredential | None = None,
    project_endpoint: str | None = None,
    model: str | None = None,
    default_options: Mapping[str, Any] | None = None,
    history_provider: HistoryProvider | None = None,
    enable_file_access: bool = True,
    file_access_store: Any = None,
    enable_shell: bool = True,
) -> Agent[Any]:
    """Build the production-ready claw harness agent.

    Args:
        stack: Async exit stack that owns optional MCP client and shell lifetimes.
        credential: Azure credential for the Foundry chat client. Defaults to AzureCliCredential.
        project_endpoint: Optional Foundry project endpoint override.
        model: Optional model deployment override.
        default_options: Optional per-agent default chat options, such as ``{"store": False}`` for hosting.
        history_provider: Optional history provider override. Hosted agents should pass
            ``InMemoryHistoryProvider(load_messages=False)`` because Responses hosting owns history.
        enable_file_access: When True (default), the agent can read and write files. Disable it on
            shared/hosted deployments where arbitrary read/write access to the container filesystem is a
            data-exfiltration and tampering risk; prefer an external ``file_access_store`` instead.
        file_access_store: Optional custom ``AgentFileStore``. When None (and ``enable_file_access`` is
            True), a ``FileSystemAgentFileStore`` rooted at the working dir is used. Supply your own — for
            example, one backed by Azure Blob Storage — to keep files off the container disk when hosted.
        enable_shell: When True (default), the agent can run shell commands. Disable it on
            shared/hosted deployments: arbitrary command execution inside the container is a serious
            security risk (data exfiltration, persistence, tampering) even behind a deny-list.

    Returns:
        A fully configured harness agent with Step 03 capabilities plus opt-in Purview middleware.
    """
    load_dotenv()
    _WORKING_DIR.mkdir(exist_ok=True)
    _VAULT_DIR.mkdir(parents=True, exist_ok=True)

    # <create_client>
    client = FoundryChatClient(
        project_endpoint=project_endpoint,
        model=model,
        credential=credential or AzureCliCredential(),
        middleware=_build_purview_middleware(),
    )
    # </create_client>

    skills_provider = await _build_skills_provider(stack)
    research_agent = _build_research_agent(client)

    if enable_shell:
        shell = _build_shell()
        print("Shell enabled (confined to the confirmations vault).")
    else:
        shell = None
        print("Shell disabled.")

    if enable_file_access:
        access_store = file_access_store or FileSystemAgentFileStore(str(_WORKING_DIR))
        print(
            "File access enabled (custom AgentFileStore)."
            if file_access_store is not None
            else "File access enabled (local filesystem)."
        )
    else:
        access_store = None
        print("File access disabled.")

    # <codeact>
    context_providers: list[Any] = [MontyCodeActProvider(approval_mode="never_require")]
    print("CodeAct enabled (Monty).")
    # </codeact>

    # <create_agent>
    return create_harness_agent(
        client=client,
        name="ClawFinanceAssistant",
        description="Production-ready personal finance claw harness agent.",
        agent_instructions=FINANCE_INSTRUCTIONS,
        tools=[get_stock_price, place_trade],
        history_provider=history_provider or InMemoryHistoryProvider(),
        disable_file_access=not enable_file_access,
        file_access_store=access_store,
        skills_provider=skills_provider,
        background_agents=[research_agent],
        shell_executor=shell,
        auto_approval_rules=[FileAccessProvider.read_only_tools_auto_approval_rule],
        context_providers=context_providers,
        mode_provider=AgentModeProvider(default_mode="execute"),
        default_options=default_options,
    )
    # </create_agent>
