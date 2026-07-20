# /// script
# requires-python = ">=3.10"
# dependencies = [
#     "agent-framework",
#     "agent-framework-tools",
#     "agent-framework-monty",
#     "agent-framework-foundry-hosting",
#     "mcp",
#     "httpx",
#     "azure-identity",
#     "python-dotenv",
# ]
# ///

# Copyright (c) Microsoft. All rights reserved.

"""Foundry Hosted Agent host for the production-ready claw.

Observability requires no exporter setup here. Agent Framework is natively instrumented (on by
default), and the Foundry hosting runtime collects and exports the traces, metrics, and logs — so
there is no ``configure_otel_providers()`` call. When deployed, Foundry injects
``APPLICATIONINSIGHTS_CONNECTION_STRING`` automatically. To capture prompt/response content, set
``ENABLE_SENSITIVE_DATA=true`` (see ``agent.yaml``). Because the exporters are Foundry-managed, run
this host with ``azd ai agent run`` to see telemetry; running it directly won't export anything.

File access and shell are disabled on the hosted container (see ``enable_file_access`` /
``enable_shell`` below).

Environment variables:
    FOUNDRY_PROJECT_ENDPOINT       — Microsoft Foundry project endpoint URL
    AZURE_AI_MODEL_DEPLOYMENT_NAME — Model deployment name for the hosted agent
    FOUNDRY_TOOLBOX_MCP_SERVER_URL — Optional Foundry Toolbox MCP endpoint URL
    PURVIEW_CLIENT_APP_ID          — Optional app/client ID; enables Purview
    ENABLE_SENSITIVE_DATA          — Enables sensitive telemetry capture (prompts/responses) when true

Run locally:
    uv run python/samples/02-agents/harness/build_your_own_claw/claw_step04_production_ready/hosted.py
"""

from __future__ import annotations

import asyncio
import os
from contextlib import AsyncExitStack

from agent_framework import InMemoryHistoryProvider
from agent_framework_foundry_hosting import ResponsesHostServer
from azure.identity import DefaultAzureCredential
from dotenv import load_dotenv

from agent import build_claw_agent


async def main() -> None:
    """Build the claw and expose it with the Foundry Responses host server."""
    load_dotenv()

    async with AsyncExitStack() as stack:
        agent = await build_claw_agent(
            stack,
            credential=DefaultAzureCredential(),
            project_endpoint=os.environ["FOUNDRY_PROJECT_ENDPOINT"],
            model=os.environ["AZURE_AI_MODEL_DEPLOYMENT_NAME"],
            default_options={"store": False},
            history_provider=InMemoryHistoryProvider(load_messages=False),
            # Disable filesystem and shell access on the hosted container. Arbitrary read/write or
            # command execution in a shared hosted environment is a serious security risk, and the
            # local confirmations vault does not exist here. To keep file access when hosted, pass an
            # external file_access_store (e.g. one backed by Azure Blob Storage) instead of the disk.
            enable_file_access=False,
            enable_shell=False,
        )
        server = ResponsesHostServer(agent)
        await server.run_async()


if __name__ == "__main__":
    asyncio.run(main())
