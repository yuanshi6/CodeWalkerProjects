# GTA Mod Agent MCP

Use the local stdio server defined in `.mcp.json`. Perform read-only inspection before any mutation. A commit requires the short-lived confirmation token returned from `operation_validate`; never reuse a token.
