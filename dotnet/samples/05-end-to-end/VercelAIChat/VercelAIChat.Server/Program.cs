// Copyright (c) Microsoft. All rights reserved.

// This sample demonstrates how to host an AI agent that is compatible with
// the Vercel AI SDK default chat transport. The /api/chat endpoint accepts
// messages in the Vercel AI SDK UIMessage format and streams responses back
// as Server-Sent Events using the UI Message Stream protocol.

using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.VercelAI.AspNetCore;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// CORS — allow the Next.js client to call the API from a different origin
// ---------------------------------------------------------------------------
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()));

// ---------------------------------------------------------------------------
// Add Vercel AI SDK protocol support
// ---------------------------------------------------------------------------
builder.Services.AddVercelAI();

// ---------------------------------------------------------------------------
// Create the AI agent with a demo weather tool using the hosted agent builder.
// WithInMemorySessionStore() registers a keyed session store so the server
// maintains conversation history and the client only sends the latest message.
// See: https://ai-sdk.dev/docs/ai-sdk-ui/storing-messages
// ---------------------------------------------------------------------------
string apiKey = builder.Configuration["OPENAI_API_KEY"]
    ?? throw new InvalidOperationException("Set the OPENAI_API_KEY environment variable.");
string model = builder.Configuration["OPENAI_MODEL"] ?? "gpt-5-mini";

var chatClientAgent = builder.Services.AddAIAgent("ChatAssistant", (sp, name) =>
    new OpenAIClient(apiKey)
        .GetChatClient(model)
        .AsAIAgent(
            name: name,
            instructions: "You are a helpful assistant. When asked about the weather, use the get_weather tool.",
            tools:
            [
                AIFunctionFactory.Create(
                    (string location) => $"The weather in {location} is 72°F and sunny.",
                    "get_weather",
                    "Get the current weather for a given location"),
            ]))
    .WithInMemorySessionStore();

WebApplication app = builder.Build();

app.UseCors();

// ---------------------------------------------------------------------------
// POST /api/chat — Vercel AI SDK–compatible streaming endpoint
// ---------------------------------------------------------------------------
app.MapVercelAI("/api/chat", chatClientAgent);

await app.RunAsync();
