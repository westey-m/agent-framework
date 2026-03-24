"use client";

import { useChat } from "@ai-sdk/react";
import { DefaultChatTransport } from "ai";
import { useState } from "react";
import { Bot, Send, User, Loader2 } from "lucide-react";

// Chat ID for the session. The server uses this to persist conversation state
// so only the latest message needs to be sent on each request.
// See: https://ai-sdk.dev/docs/ai-sdk-ui/storing-messages#sending-only-the-last-message
const CHAT_ID = "default-chat";

export default function ChatPage() {
  const { messages, sendMessage, status, error } = useChat({
    id: CHAT_ID,
    transport: new DefaultChatTransport({
      api: "/api/chat",
      // Send only the latest message and the chat ID instead of the full
      // message history. The server maintains conversation state via sessions.
      // See: https://ai-sdk.dev/docs/ai-sdk-ui/chatbot#transport-configuration
      prepareSendMessagesRequest({ id, messages }) {
        return { body: { id, message: messages[messages.length - 1] } };
      },
    }),
  });

  const [input, setInput] = useState("");

  const isLoading = status === "submitted" || status === "streaming";

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim() || isLoading) return;
    sendMessage({ text: input });
    setInput("");
  };

  return (
    <div className="flex flex-col h-screen max-w-3xl mx-auto">
      {/* Header */}
      <header className="border-b px-6 py-4 flex items-center gap-3">
        <Bot className="w-6 h-6 text-blue-600" />
        <h1 className="text-lg font-semibold">
          Agent Framework + Vercel AI SDK
        </h1>
      </header>

      {/* Messages */}
      <div className="flex-1 overflow-y-auto px-6 py-4 space-y-6">
        {messages.length === 0 && (
          <div className="text-center text-gray-400 mt-20">
            <Bot className="w-12 h-12 mx-auto mb-4 opacity-50" />
            <p>Send a message to start chatting with the AI agent.</p>
            <p className="text-sm mt-1">
              Try asking about the weather in a city!
            </p>
          </div>
        )}

        {messages.map((message) => (
          <div
            key={message.id}
            className={`flex gap-3 ${
              message.role === "user" ? "justify-end" : "justify-start"
            }`}
          >
            {message.role !== "user" && (
              <div className="w-8 h-8 rounded-full bg-blue-100 flex items-center justify-center flex-shrink-0">
                <Bot className="w-4 h-4 text-blue-600" />
              </div>
            )}

            <div
              className={`rounded-2xl px-4 py-2.5 max-w-[80%] ${
                message.role === "user"
                  ? "bg-blue-600 text-white"
                  : "bg-gray-100 text-gray-900 dark:bg-gray-800 dark:text-gray-100"
              }`}
            >
              {message.parts.map((part, index) => {
                if (part.type === "text") {
                  return (
                    <span key={index} className="whitespace-pre-wrap">
                      {part.text}
                    </span>
                  );
                }
                if (part.type.startsWith("tool-")) {
                  return (
                    <div
                      key={index}
                      className="my-2 p-2 rounded bg-gray-200 dark:bg-gray-700 text-xs font-mono"
                    >
                      <div className="font-semibold">
                        🔧 {part.type.slice(5)}
                      </div>
                      {"output" in part && part.output != null && (
                        <div className="mt-1 text-green-700 dark:text-green-400">
                          {JSON.stringify(part.output)}
                        </div>
                      )}
                    </div>
                  );
                }
                return null;
              })}
            </div>

            {message.role === "user" && (
              <div className="w-8 h-8 rounded-full bg-blue-600 flex items-center justify-center flex-shrink-0">
                <User className="w-4 h-4 text-white" />
              </div>
            )}
          </div>
        ))}

        {status === "submitted" && (
          <div className="flex gap-3 items-center">
            <div className="w-8 h-8 rounded-full bg-blue-100 flex items-center justify-center">
              <Bot className="w-4 h-4 text-blue-600" />
            </div>
            <Loader2 className="w-5 h-5 animate-spin text-gray-400" />
          </div>
        )}

        {error && (
          <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4 text-red-700 dark:text-red-300 text-sm">
            Something went wrong. Please try again.
          </div>
        )}
      </div>

      {/* Input */}
      <form onSubmit={handleSubmit} className="border-t px-6 py-4">
        <div className="flex gap-2">
          <input
            type="text"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            placeholder="Type a message…"
            disabled={isLoading}
            className="flex-1 rounded-full border px-4 py-2.5 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50 dark:bg-gray-800 dark:border-gray-700"
          />
          <button
            type="submit"
            disabled={isLoading || !input.trim()}
            className="rounded-full bg-blue-600 text-white p-2.5 hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            <Send className="w-4 h-4" />
          </button>
        </div>
      </form>
    </div>
  );
}
