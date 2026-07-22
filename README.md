# GTA Mod Agent MCP

面向 GTA V 单机模组的本地 MCP（Model Context Protocol）服务。它复用 CodeWalkerProjects 的 RPF 解析能力，并为后续 OIV 安装工作流提供基础，为 Codex、Claude Code 等 Agent 提供**可观察、受控、可验证、可回滚**的模组文件操作能力。

作者：**yuanshi6**

> 本项目仅面向 GTA V 单机模组管理。不会提供 GTA Online 相关自动化，也不会允许 Agent 执行任意命令或写入原版游戏档案。

## 已实现功能

### 读取、查询与分析

- 扫描 GTA V 安装目录，并识别 Legacy / Enhanced 版本。
- 列出 RPF 目录或完整树：`rpf_list`、`rpf_tree`。
- 搜索、查看统计信息、读取文本/XML、提取受大小限制的文件：`rpf_search`、`rpf_stat`、`rpf_read_text`、`rpf_read_xml`、`rpf_extract`。
- 获取资源基础信息和 SHA-256 哈希：`resource_inspect`。
- 分析目录、ZIP、RAR、7z、OIV 与 RPF 模组包：`mod_inspect`。
- 校验 Add-on Ped 的 `.ydd`、`.yft`、`.ymt`、`.ytd` 文件是否齐全且命名一致：`ped_analyze`。
- 在临时工作区生成独立 Ped DLC RPF；不会直接写入游戏：`ped_build_addon`。

### 受控写入与回滚

写入不会直接执行。必须按以下流程操作：

```text
operation_create
  → rpf_stage_add / replace / delete / text_edit / xml_edit
  → operation_validate
  → operation_commit（携带一次性确认令牌）
```

- 支持对 `mods` 中的普通文件或 RPF 内部文件新增、替换、删除与文本/XML 修改。
- `operation_validate` 会检查游戏进程、磁盘空间、路径和文件大小，并签发有效期 10 分钟的一次性确认令牌。
- 提交前建立备份；提交失败时自动回滚。
- 可通过 `operation_get`、`operation_list` 查看记录；`operation_rollback` 恢复已写入内容，`operation_cancel` 仅取消尚未提交的事务。
- SQLite 保存事务、备份清单与调用审计。

## 安全边界

- 原版 GTA 文件始终只读；仅允许写入 `<GTA 目录>\mods`。
- 拒绝 `..` 路径穿越与符号链接逃逸。
- GTA V 运行时拒绝写入。
- 同一档案同一时间仅允许一个写事务。
- 不提供 PowerShell、任意 C# 方法调用或任意路径写入工具。

## 快速开始

### 环境要求

- Windows 10/11
- .NET SDK 8.0
- 已安装 GTA V PC 版（Legacy 或 Enhanced）

### 配置

编辑 [config/agentsettings.json](config/agentsettings.json)，填写你的游戏路径：

```json
{
  "Game": {
    "LegacyPath": "D:\\SteamLibrary\\steamapps\\common\\Grand Theft Auto V",
    "EnhancedPath": "",
    "PreferredEdition": "auto"
  }
}
```

### 构建与运行

```powershell
dotnet build CodeWalker.Agent.Mcp/CodeWalker.Agent.Mcp.csproj
dotnet run --project CodeWalker.Agent.Cli/CodeWalker.Agent.Cli.csproj -- scan "D:\SteamLibrary\steamapps\common\Grand Theft Auto V"
```

仓库根目录的 [.mcp.json](.mcp.json) 可作为 Codex/Claude Code 的本地 stdio MCP 配置示例。MCP 协议数据使用 stdout；服务日志仅写入 stderr。

## 项目结构

```text
CodeWalker.Agent.Abstractions  接口、DTO、错误码与统一工具结果
CodeWalker.Agent.Core          GTA/RPF 读取、模组分析、事务化写入
CodeWalker.Agent.Security      mods 白名单、进程保护、路径与档案锁
CodeWalker.Agent.Storage       SQLite 事务、备份和审计记录
CodeWalker.Agent.Mcp           stdio MCP Server 与工具注册
CodeWalker.Agent.Cli           本地调试命令行入口
CodeWalker.Agent.Peds          Ped 文件集验证模型
CodeWalker.Agent.Preview       预览工作进程入口（开发中）
CodeWalker.Agent.Worker        耗时任务工作进程入口（开发中）
CodeWalker.Agent.Tests         单元与临时 RPF 集成测试
```

## 当前限制

以下能力尚未完成，不应视为可用功能：

- YTD 纹理 PNG、YFT/YDD/YDR 离屏 3D 截图与地图截图预览。
- 一键 OIV 安装/卸载。
- Add-on DLC 的自动启用、禁用和卸载工作流。
- 后台 RPF 全量索引、批量预览与诊断报告。

当前可通过底层事务工具安全完成受控写入；高层安装工作流会在后续版本接入同一事务与回滚机制。

## 开发与验证

```powershell
dotnet test CodeWalker.Agent.Tests/CodeWalker.Agent.Tests.csproj
```

测试使用临时目录与新建测试 RPF，不会对真实 GTA 档案执行写入。

更多 MCP 使用和安全说明见 [docs/GTA-Mod-Agent-MCP.md](docs/GTA-Mod-Agent-MCP.md)。

## 致谢与许可

本项目基于 [CodeWalkerProjects](https://github.com/yuanshi6/CodeWalkerProjects) 的 CodeWalker 基础代码与文件格式处理能力构建。请保留原项目及其作者 dexyfex 的版权、许可和第三方依赖声明；完整信息见 [Notice.txt](Notice.txt)。
