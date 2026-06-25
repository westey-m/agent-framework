# Build your own agent harness and claw — Python samples

Runnable Python samples for the [**"Build your own agent harness and claw with Microsoft Agent Framework"** blog](https://devblogs.microsoft.com/agent-framework/build-your-own-claw-and-agent-harness-with-microsoft-agent-framework)
series. Each step builds a personal finance / investing assistant on top of
`create_harness_agent`, reusing the shared harness `console` package in the parent `harness/`
directory.

- **Part 1 — `claw_step01_meet_your_claw.py`** — the minimal harness.
- **Part 3 — `claw_step03_scaling_capabilities.py`** — skills, shell, CodeAct, and background agents.

## Prerequisites

1. A Microsoft Foundry project with a deployed model.
2. Azure CLI installed and authenticated (`az login`).

## Environment variables

```bash
export FOUNDRY_PROJECT_ENDPOINT="https://your-project.services.ai.azure.com/api/projects/your-project"
export FOUNDRY_MODEL="your-model-deployment-name"
```

---

## Part 1 — Meet your claw

Builds the foundation of the assistant on top of `create_harness_agent`.

### What this sample demonstrates

- **`create_harness_agent`** — a factory that builds a batteries-included agent: function
  invocation, per-service-call history persistence, planning (`TodoProvider` +
  `AgentModeProvider`), and web search.
- **A custom function tool** — `get_stock_price`, exposing local data to the agent. Prices are
  illustrative mock data, not real quotes.
- **Web search** — provided automatically by the harness for market news and commentary.
- **Planning & modes** — the agent breaks a multi-step request ("review my watchlist") into a todo
  list and switches between *plan* and *execute* modes.
- **Shared harness console** — interactive streaming UI (reused from the parent `harness/console`
  package) with `/todos`, `/mode`, and `/exit` commands.

### Running

```bash
# From the repository root, using a PEP 723 compatible runner:
uv run python/samples/02-agents/harness/build_your_own_claw/claw_step01_meet_your_claw.py
```

### What to expect

The sample starts an interactive loop. Try these in order:

1. `/mode execute` — switch out of the default plan mode; quick lookups don't need a plan.
2. `What's the price of MSFT?` — the agent calls the `get_stock_price` tool.
3. `Any recent news on NVDA?` — the agent uses web search.
4. `Add MSFT, NVDA and SPY to my watch list` — saved to `watchlist.md` in the session's memory.
5. `/mode plan` — switch back to plan mode for a bigger, multi-step task.
6. `Review my watchlist and recommend some stocks to add` — the agent plans, then executes. Type
   `/todos` to see the list and `/mode` to inspect the current mode.

---

## Part 3 — Scaling its capabilities

Makes the assistant *more capable* along four axes.

### What this sample demonstrates

- **Skills** — finance know-how (`valuation`, `risk-scoring`) is packaged as discoverable
  `SKILL.md` files under `skills/`, which the agent loads on demand. The sample builds a
  `FileSkillsSource(..., script_runner=subprocess_script_runner)` so the skills' Python
  scripts can run. Optionally folds in centrally-managed **Foundry skills** served from a
  Foundry Toolbox MCP endpoint via `MCPSkillsSource` (opt-in; see below).
- **Shell** — a `LocalShellTool` confined to the trade-confirmation vault
  (`working/confirmations/`) lets the agent tidy the accumulated confirmation files (reorganize into
  `year/month`, rename to `YYYY-MM-DD_TICKER_BUY|SELL.txt`). Guarded by a `ShellPolicy` deny-list
  **and** a confined working directory; left at the default
  `approval_mode="always_require"` so each command is surfaced for approval.
- **CodeAct** — a `MontyCodeActProvider` gives the agent a sandboxed, cross-platform Python
  interpreter to crunch portfolio numbers by writing and running code.
- **Background agents** — a lean, web-search-only `TickerResearchAgent` is registered via
  `create_harness_agent(background_agents=[...])`, so the main agent can fan out per-ticker research
  concurrently and aggregate the findings.

### Additional environment variables (optional)

```bash
# Enable centrally-managed Foundry skills (Foundry Toolbox MCP endpoint URL):
export FOUNDRY_TOOLBOX_MCP_SERVER_URL="https://<your-project>.services.ai.azure.com/.../toolboxes/<toolbox>/mcp?api-version=v1"
```

When this is not set, the sample runs with the local file skills only, and prints a note.

### Running

```bash
uv run python/samples/02-agents/harness/build_your_own_claw/claw_step03_scaling_capabilities.py
```

### What to expect

Try these in order (the sample starts in **execute** mode — quick lookups don't need a plan):

1. `Value MSFT for me.` — the agent loads the `valuation` skill and follows its instructions
   (reading references and running its script).
2. `Score the risk of my portfolio.` — the agent reads `portfolio.csv` and loads the `risk-scoring`
   skill.
3. `/mode plan`, then `Tidy up my trade confirmations.` — switching to plan mode first makes the
   agent inspect `working/confirmations/` and propose a reorganization plan before touching anything;
   once you approve it switches to execute and uses the shell to reorganize and rename the files,
   **prompting you to approve** each command.
4. `Work out the total value of my portfolio.` — the agent writes and runs Python via CodeAct.
5. `Research MSFT, NVDA and SPY and summarize the latest news.` — the agent fans the tickers out to
   the background research agent and aggregates the results.
6. `What's the capital of France?` — with a `financial-agent-rules` skill published to your Foundry
   toolbox and Foundry skills enabled (`FOUNDRY_TOOLBOX_MCP_SERVER_URL`), the agent loads it,
   recognizes the question is off-topic, and politely declines, steering you back to finance.

See the [Part 3 blog post](https://devblogs.microsoft.com/agent-framework/agent-harness-scaling-its-capabilities)
for more on the `financial-agent-rules` skill — including the SKILL.md to publish to your Foundry toolbox.
