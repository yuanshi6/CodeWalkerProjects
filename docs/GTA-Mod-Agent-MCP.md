# GTA Mod Agent MCP

## Quick start

1. Copy `config/agentsettings.json` and set `Game.LegacyPath` or `Game.EnhancedPath` to a GTA V installation.
2. Build `CodeWalker.Agent.Mcp` and add the repository `.mcp.json` to your MCP client.
3. Call `gta_scan_installation`, then use `rpf_list`, `rpf_search`, `resource_inspect`, or `mod_inspect` for read-only work.

## Safe writes

All write requests must create an operation, stage narrowly scoped changes under `mods`, validate the plan, and commit with its one-time token. The server rejects vanilla paths, traversal, symlinked targets, concurrent archive writes, and writes while GTA is running. On failure it restores backups and records the operation as `rolled_back`.

## Current tools

The MCP implementation exposes game discovery, RPF listing/search/stat/extraction/text/XML, resource inspection, package and Ped analysis/build, and transaction staging/validation/commit/rollback. Large binary extraction is returned only as bounded base64 data.
