# Auth Client-Server Sample

This sample demonstrates how to secure an AI agent REST API with standards-based authentication and authorization using OAuth 2.0 / OpenID Connect, JWT Bearer tokens, and policy-based scope enforcement.

While this sample uses Keycloak to avoid complex setup in order to run the sample, Keycloak can easily be replaced with any OIDC compatible provider, including [Microsoft Entra Id](https://www.microsoft.com/security/business/identity-access/microsoft-entra-id).

## Overview

The sample has three components, all launched with a single `docker compose up`:

| Service | Port | Description |
|---------|------|-------------|
| **WebClient** | `http://localhost:8080` | Razor Pages web app with OIDC login and a chat UI that calls the AgentService |
| **AgentService** | `http://localhost:5001` | ASP.NET Minimal API hosting a `ChatClientAgent`, secured with JWT Bearer auth and scope-based policies |
| **Keycloak** | `http://localhost:5002` | OIDC identity provider, auto-provisioned with realm, clients, scopes, and test users |

```
┌──────────────┐     OIDC login       ┌───────────┐
│  WebClient   │ ◄──────────────────► │ Keycloak  │
│  (Razor app) │     (browser flow)   │ (Docker)  │
│  :8080       │                      │ :5002     │
└──────┬───────┘                      └─────┬─────┘
       │ REST + Bearer token                │
       ▼                                    │
┌───────────────┐   JWT validation    ──────┘
│ AgentService  │ ◄──── (jwks from Keycloak)
│ (Minimal API) │
│ :5001         │
└───────────────┘
```

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) and Docker Compose

## Configuring Environment Variables

The AgentService requires an OpenAI-compatible endpoint. Set these environment variables before running:

```bash
export OPENAI_API_KEY="<your-openai-api-key>"
export OPENAI_MODEL="gpt-4.1-mini"
```

## Running the Sample

### Option 1: Docker Compose (Recommended)

```bash
cd samples/AuthClientServer
docker compose up
```

This starts Keycloak, the AgentService, and the WebClient. Wait for Keycloak to finish importing the realm (you'll see `Running the server` in the logs).

#### Running in GitHub Codespaces

This sample has been built in such a way that it can be run from GitHub Codespaces.
The Agent Framework repository has a C# specific dev container, named "C# (.NET)", that is configured for Codespaces.

When running in Codespaces, the sample auto-detects the environment via
`CODESPACE_NAME` and `GITHUB_CODESPACES_PORT_FORWARDING_DOMAIN` and configures
Keycloak and the web client accordingly. Just make the required ports public:

```bash
# Make Keycloak and WebClient ports publicly accessible
gh codespace ports visibility 5002:public 8080:public -c $CODESPACE_NAME

# Start the containers (Codespaces is auto-detected)
docker compose up
```

Then open the Codespaces-forwarded URL for port 8080 (shown in the **Ports** tab) in your browser.

### Option 2: Run Locally

1. Start Keycloak:
   ```bash
   docker compose up keycloak
   ```

2. In a new terminal, start the AgentService:
   ```bash
   cd AuthClientServer.AgentService
   dotnet run --urls "http://localhost:5001"
   ```

3. In another terminal, start the WebClient:
   ```bash
   cd AuthClientServer.WebClient
   dotnet run --urls "http://localhost:8080"
   ```

## Using the Sample

1. Open `http://localhost:8080` in your browser
2. Click **Login** — you'll be redirected to Keycloak
3. Sign in with one of the pre-configured users:
   - **`testuser` / `password`** — has the `agent.chat` scope, can chat with the agent
   - **`viewer` / `password`** — lacks the `agent.chat` scope, will receive a 403 Forbidden when trying to chat
4. Type a message and click **Send** to chat with the agent

## Pre-Configured Keycloak Realm

The `keycloak/dev-realm.json` file auto-provisions:

| Resource | Details |
|----------|---------|
| **Realm** | `dev` |
| **Client: agent-service** | Confidential client (the API audience) |
| **Client: web-client** | Public client for the Razor app's OIDC login |
| **Scope: agent.chat** | Required to call the `/chat` endpoint |
| **User: testuser** | Has `agent.chat` scope |
| **User: viewer** | Does not have `agent.chat` scope |

Keycloak admin console: `http://localhost:5002` (login: `admin` / `admin`).

## API Endpoints

### POST /chat (requires `agent.chat` scope)

```bash
# Get a token for testuser
TOKEN=$(curl -s -X POST http://localhost:5002/realms/dev/protocol/openid-connect/token \
  -d "grant_type=password&client_id=web-client&username=testuser&password=password&scope=openid agent.chat" \
  | jq -r '.access_token')

# Chat with the agent
curl -X POST http://localhost:5001/chat \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"message": "Hello, what can you help me with?"}'
```

### GET /me (requires any valid token)

```bash
curl http://localhost:5001/me -H "Authorization: Bearer $TOKEN"
```

## Key Concepts Demonstrated

- **JWT Bearer Authentication** — The AgentService validates tokens from Keycloak using OIDC discovery
- **Policy-Based Authorization** — The `/chat` endpoint requires the `agent.chat` scope in the token
- **Caller Identity** — The service reads the caller's identity from `HttpContext.User` (ClaimsPrincipal)
- **OIDC Login Flow** — The WebClient uses OpenID Connect authorization code flow with Keycloak
- **Token Forwarding** — The WebClient stores the access token and sends it as a Bearer token to the AgentService
