# Claw Step 04 — Production-ready

This folder restructures the Step 03 claw into a shared agent module plus thin hosts. It is also a
self-contained Foundry deployment package (Docker build context):

- `agent.py` — `build_claw_agent(stack, ...)` builds the full Step 03 claw and adds opt-in Purview chat middleware.
- `console.py` — local interactive Textual console with OpenTelemetry provider setup.
- `hosted.py` — Foundry Hosted Agent entry point using `ResponsesHostServer`.
- `evals.py` — local finance checks plus optional Foundry evaluators.
- `Dockerfile` — image Foundry builds for the hosted agent; its `CMD` selects the entry point.
- `requirements.txt` — packages installed into the hosted image.
- `.dockerignore` — files excluded from the image (caches, `.env`, the local `working/` vault).
- `skills/` and `subprocess_script_runner.py` — local copies so the folder is a self-contained build
  context (the parent sample folder can't be reached from a Docker `COPY .`).

## Environment

```bash
export FOUNDRY_PROJECT_ENDPOINT="https://your-project.services.ai.azure.com/api/projects/your-project"
export FOUNDRY_MODEL="your-local-model-deployment"
export AZURE_AI_MODEL_DEPLOYMENT_NAME="your-hosted-model-deployment"
```

Optional:

```bash
export FOUNDRY_TOOLBOX_MCP_SERVER_URL="https://.../mcp?api-version=v1"
export PURVIEW_CLIENT_APP_ID="your-purview-app-client-id"
export ENABLE_CONSOLE_EXPORTERS="true"
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317"
```

## Run locally

```bash
uv run python/samples/02-agents/harness/build_your_own_claw/claw_step04_production_ready/console.py
```

## Host with Foundry

```bash
uv run python/samples/02-agents/harness/build_your_own_claw/claw_step04_production_ready/hosted.py
```

The hosted version **disables file access and shell** on the container. In a shared, hosted environment, giving the model arbitrary read/write access to the container filesystem or letting it run shell commands is a serious security risk (data exfiltration, tampering, persistence), and the local confirmations vault the shell operates on doesn't exist there. Background agents and Monty CodeAct (alpha) remain enabled. If you need file access when hosted, pass an external `file_access_store` (for example, one backed by Azure Blob Storage) instead of the container disk.

### Deploy to Foundry

```bash
azd ai agent init -m python/samples/02-agents/harness/build_your_own_claw/claw_step04_production_ready/agent.manifest.yaml
azd deploy
```

Foundry deploys hosted agents as containers. It builds the image from this folder's `Dockerfile`,
and the Dockerfile's `CMD ["python", "hosted.py"]` is what tells Foundry to run `hosted.py` — not
`console.py` or `evals.py`. There is no folder convention or `agent.yaml` field that picks the entry
point; it is whatever `CMD` names. The `Dockerfile` does `COPY . user_agent/`, so the build context
is this folder only — it can't reach the shared parent sample folder. That's why `skills/` and
`subprocess_script_runner.py` are copied in here, making the folder a self-contained package.

## Run evals

```bash
uv run python/samples/02-agents/harness/build_your_own_claw/claw_step04_production_ready/evals.py
```

Local evals use `LocalEvaluator` custom checks. When `FOUNDRY_PROJECT_ENDPOINT` is set, the sample also runs `FoundryEvals` with relevance and coherence.

## Observability and Purview

The **local** hosts (`console.py`, `evals.py`) call `configure_otel_providers()` from `agent_framework.observability`, which honors `ENABLE_INSTRUMENTATION`, `ENABLE_SENSITIVE_DATA`, `ENABLE_CONSOLE_EXPORTERS`, and OTLP endpoint environment variables.

The **hosted** host (`hosted.py`) wires no exporters: Agent Framework instrumentation is on by default and the Foundry hosting runtime collects and exports telemetry. Foundry injects `APPLICATIONINSIGHTS_CONNECTION_STRING` when deployed; set `ENABLE_SENSITIVE_DATA=true` to include prompt/response content. Because the exporters are Foundry-managed, run the hosted host with `azd ai agent run` to see telemetry.

Purview is opt-in. When `PURVIEW_CLIENT_APP_ID` is set, `agent.py` creates `InteractiveBrowserCredential(client_id=...)` and attaches `PurviewChatPolicyMiddleware(..., PurviewSettings(app_name="Claw"))` to `FoundryChatClient`. Otherwise it prints a note and runs without policy middleware.
