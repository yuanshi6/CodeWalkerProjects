# GTA Mod Agent rules

- Treat all GTA original archives as read-only. Only transaction commits may change paths under `<game>\mods`.
- Never call a shell, arbitrary C# method, or UI automation through this MCP server.
- Use `operation_create` → stage tools → `operation_validate` → `operation_commit`; preserve the returned operation ID for rollback.
- Do not install unknown packages. Run `mod_inspect` and, for Ped packages, `ped_analyze` first.
- Logs go to stderr because stdout is reserved for MCP JSON-RPC.
