# Vercel AI SDK + .NET Agent Framework Chat Sample

This end-to-end sample demonstrates a **Next.js** frontend using the [Vercel AI SDK](https://ai-sdk.dev/) (`useChat` hook) communicating with a **.NET Agent Framework** backend over the [Vercel AI UI Message Stream protocol](https://ai-sdk.dev/docs/ai-sdk-ui/transport).

```
┌──────────────────┐          POST /api/chat           ┌──────────────────────┐
│                  │  ──────────────────────────────►  │                      │
│   Next.js App    │                                   │   ASP.NET Server     │
│   (port 3000)    │  ◄────────────────────────────    │   (port 5001)        │
│                  │     SSE: UIMessageStream          │                      │
│  • useChat hook  │                                   │  • MapVercelAI()     │
│  • Tailwind UI   │                                   │  • ChatClientAgent   │
│                  │                                   │  • OpenAI provider   │
└──────────────────┘                                   └──────────────────────┘
```

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) and Docker Compose
- An [OpenAI API key](https://platform.openai.com/api-keys)

## Quick Start

1. **Set your API key**

   ```bash
   export OPENAI_API_KEY=sk-...
   ```

2. **Start the containers**

   ```bash
   docker compose up --build
   ```

3. **Open the chat UI**

   Navigate to [http://localhost:3000](http://localhost:3000) in your browser.

## Configuration

| Variable | Default | Description |
|---|---|---|
| `OPENAI_API_KEY` | *(required)* | Your OpenAI API key |
| `OPENAI_MODEL`   | `gpt-5-mini` | OpenAI model to use |

## Architecture

### Server (`VercelAIChat.Server`)

A minimal ASP.NET application that:

- Creates a `ChatClientAgent` backed by OpenAI with a demo weather tool
- Maps `POST /api/chat` using the `MapVercelAI()` extension from `Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore`
- Converts incoming Vercel AI SDK messages to `ChatMessage` objects
- Streams responses back as Server-Sent Events in the UI Message Stream format

### Client (`VercelAIChat.Client`)

A Next.js application that:

- Uses the `useChat` hook from `@ai-sdk/react` with `DefaultChatTransport`
- Renders a chat UI with message bubbles, tool invocation display, and streaming indicators
- Communicates directly with the .NET backend (no BFF proxy needed)

### Protocol

The Vercel AI SDK default transport sends a `POST` request with `{ id, messages, trigger, messageId }` and expects an SSE response with `data: <UIMessageChunk JSON>\n\n` lines ending with `data: [DONE]\n\n`.

The `Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore` library implements this protocol on the .NET side, converting between the Vercel AI SDK message format and the Agent Framework's `ChatMessage`/`ChatResponseUpdate` types.

## Running Without Docker

### Server

```bash
cd VercelAIChat.Server
export OPENAI_API_KEY=sk-...
dotnet run
```

### Client

```bash
cd VercelAIChat.Client
npm install
NEXT_PUBLIC_API_URL=http://localhost:5001 npm run dev
```
