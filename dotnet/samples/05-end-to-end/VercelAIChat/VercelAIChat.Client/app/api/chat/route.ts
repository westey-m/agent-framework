// Proxy route that forwards chat requests to the .NET backend.
// This lets the browser call its own origin (/api/chat) so the app
// works in GitHub Codespaces, local Docker, and bare-metal dev
// without configuring cross-origin URLs.

const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:5001";

export async function POST(req: Request) {
  const response = await fetch(`${BACKEND_URL}/api/chat`, {
    method: "POST",
    headers: { "Content-Type": req.headers.get("Content-Type") ?? "application/json" },
    body: await req.text(),
  });

  return new Response(response.body, {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "text/plain" },
  });
}
