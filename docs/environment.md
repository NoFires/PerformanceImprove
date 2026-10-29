# 工具与 MCP 环境

2026-10-21 Node 运行环境已完整复制到 `E:\Program\Development\node`（1807个文件逐项SHA-256一致），Codex/dsh MCP、pnpm/dsh启动器、逆向技能及维护脚本共12处引用已更新，用户PATH已加入新目录。按用户指示结束了旧路径Node进程。旧目录仅有读取权限，系统PATH也拒绝非管理员写入，因此旧目录清理及系统PATH替换仍待管理员执行 `E:\Program\Development\Complete-NodeMigration-AsAdmin.ps1`；尚不能记为完整迁移。备份与核验在 `E:\Program\Development\environment-migrations\node-20261021-224337`。脚本会先校验全部旧文件在新目录中的副本，再更新系统PATH和删除旧目录；执行后重开终端/Codex。未修改游戏文件。

2026-10-21 已新增 Ghidra、x64dbg 及两个社区 MCP 的安装与真实连接验证，分类路径、启动入口和限制见 [原生逆向工具与 MCP](native-tools.md)。下文记录的是原有托管分析工具环境。

## 已配置内容

复用了已有 .NET SDK（10.0.100，另有 9.0.306）、Python 3.12.4 和 Node；没有另装 Unity 编辑器或覆盖已有 SDK。

| 路径 | 用途 |
|---|---|
| `E:\Program\Development\WrathPerformance` | 当前完整项目：源码、测试、反编译、分析、安装包与备份 |
| `E:\Program\ReverseEngineering\ILSpyCmd` | 固定版本 ilspycmd 11.0.0.9375，命令行反编译器 |
| `E:\Program\MCP\wrath-analysis` | 本地分析 MCP 的部署副本、虚拟环境、依赖锁文件 |
| `E:\Program\MCP\backups` | 修改 MCP 前备份的 Codex config.toml |
| `E:\Program\Caches\NuGet\packages` | .NET 工具安装期间设置的 NuGet 包缓存 |
| `E:\Program\Caches\NuGet\http` | NuGet HTTP 缓存 |
| `E:\Program\Caches\pip` | pip 下载缓存 |
| `E:\Program\Development\dotnet-home` | 工具安装期间设置的 DOTNET_CLI_HOME |

缓存环境变量只用于相应安装命令，没有修改系统级 PATH 或全局环境变量。未来自行运行 dotnet 命令时，如需将缓存继续放 E 盘，应按下文设置变量。

ILSpy 固定版本来自官方 NuGet 包。安装时查询到更高版本 11.1.0.9782，但本工作区保持已经验证过的 11.0.0.9375；后续升级应显式修改版本并复测。

## MCP 的实际状态

通过 `codex mcp add` 添加了用户级服务器 `wrath-analysis`。配置文件：`C:\Users\RedAndBlack\.codex\config.toml`。原有 node_repl 等配置保留，备份放在上述 backups 目录。

```toml
[mcp_servers.wrath-analysis]
command = 'E:\Program\MCP\wrath-analysis\.venv\Scripts\python.exe'
args = ['-u', 'E:\Program\MCP\wrath-analysis\server.py']

[mcp_servers.wrath-analysis.env]
WRATH_WORKSPACE = 'E:\Program\Development\WrathPerformance'
```

服务器使用官方 Python MCP SDK 1.30.0，stdio 传输。没有监听 TCP 端口，不需要 API key，不上传游戏文件。源码在工作区 `mcp/server.py`，部署副本在 E 盘，配置工具可以通过环境变量 WRATH_GAME_ROOT、WRATH_WORKSPACE、WRATH_ILSPY 指定路径。

| 工具 | 功能 |
|---|---|
| game_inventory | 读取有日期的版本/DLL/Mod 快照 |
| list_types | 从指定本机游戏 DLL 枚举类型并过滤 |
| decompile_type | 按全名反编译类型，支持分页 |
| search_source | 在已反编译的指定程序集内按文字检索 |
| read_source | 按路径和行号读取 C#，阻止越出反编译根目录 |

五个工具均标为只读。DLL 分析限于本游戏 Managed/Mods 目录，不能任意读取别的程序集路径；反编译器作为单独进程读取元数据与 IL，不加载游戏程序集执行逻辑。子进程有超时，输出有分页/条数限制。

已完成 SDK 客户端真实 stdio 初始化、工具发现、五个工具逐个调用，以及路径越界拒绝测试。协议协商为 2025-11-25。报告见 `analysis/mcp-verification.json`。本次 Codex 会话随后实际出现了 MCP 工具，并成功通过其 decompile_type 调用了 Kingmaker.GameVersion。若其他已有会话未显示新工具，可重新打开会话/应用以重新连接。

## 复现命令

以下命令在 `E:\Program\Development\WrathPerformance` 运行：

```powershell
# 更新版本、程序集/Mod 哈希快照
python .\scripts\inventory.py

# 重新反编译八个目标程序集；不会修改游戏
.\scripts\decompile.ps1

# 重新统计结构、控制器注册与旧启动日志
python .\scripts\summarize.py

# 从锁文件配置 MCP，并测试握手/调用
.\scripts\install-mcp.ps1

# 检查注册信息
codex mcp get wrath-analysis

# 单独复测 MCP
& 'E:\Program\MCP\wrath-analysis\.venv\Scripts\python.exe' .\scripts\test_mcp.py
```

ILSpy 的初始安装命令及缓存位置：

```powershell
$env:NUGET_PACKAGES = 'E:\Program\Caches\NuGet\packages'
$env:NUGET_HTTP_CACHE_PATH = 'E:\Program\Caches\NuGet\http'
$env:DOTNET_CLI_HOME = 'E:\Program\Development\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet tool install ilspycmd --version 11.0.0.9375 --tool-path 'E:\Program\ReverseEngineering\ILSpyCmd'
```

游戏更新或切换 Mod 后，应重新运行 inventory、decompile、summarize，并检查新签名。反编译脚本写入同一输出目录，旧版本可能残留已删除类型；跨版本分析时应先将旧输出目录改名归档，再生成新输出，避免混用。

卸载本次 MCP 注册只需 `codex mcp remove wrath-analysis`；这不影响游戏文件。E 盘工具目录可保留供后续复用。

## 官方资料

- [ILSpy 官方项目](https://github.com/icsharpcode/ILSpy)
- [ilspycmd 固定版本 NuGet](https://www.nuget.org/packages/ilspycmd/11.0.0.9375)
- [Codex MCP 配置文档](https://developers.openai.com/codex/mcp/)
- [MCP Python SDK](https://github.com/modelcontextprotocol/python-sdk)
- [MCP stdio 传输规范](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)
