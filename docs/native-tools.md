# 原生逆向工具与 MCP

安装与验证日期：2026-10-21。仅安装分析环境并用自建测试程序验证，没有启动或附加游戏，没有修改游戏文件。

## 安装分类

| 类别 | 路径 | 内容 |
|---|---|---|
| 原生静态分析 | `E:\Program\ReverseEngineering\Ghidra\ghidra_12.1.2_PUBLIC` | Ghidra 12.1.2；匹配 GhidraMCP 6.0.0 的发布版本 |
| 原生动态调试 | `E:\Program\ReverseEngineering\x64dbg\2026-05-27` | 官方快照，包含 x64dbg / x32dbg |
| Ghidra MCP | `E:\Program\MCP\ghidra-mcp` | 6.0.0 扩展、独立 Python venv、启动器、日志、依赖锁与参考文档 |
| x64dbg MCP | `E:\Program\MCP\x64dbg-mcp` | 2.3.0 Node 桥接，package-lock.json 固定依赖 |
| 测试目标 | `E:\Program\MCP\TestTargets` | 本工作区 `tests/native_mcp_smoke.c` 编译的独立 EXE |
| 下载缓存 | `E:\Program\Caches\ReverseEngineering` | 官方工具 ZIP、社区插件 ZIP / wheel / dp64 / dp32 |
| Java 临时文件 | `E:\Program\Caches\Ghidra\tmp` | 启动器指定的短路径，解决本机 Java NIO 回环连接失败 |
| 其他依赖缓存 | `E:\Program\Caches\pip`、`E:\Program\Caches\npm` | Python / Node 下载缓存 |

复用 `E:\Program\java21` 和 `E:\Program\Development\node`，没有安装新的 Java 或 Node，没有改全局 PATH。Ghidra 的 `support/launch.properties` 固定使用现有 Java 21，原文件保留为 `.original`。JDK 25 曾用于排查同一连接错误，最终运行使用 Java 21。

Ghidra 的常规用户偏好及 application.log 仍由程序写入 `%APPDATA%\ghidra\ghidra_12.1.2_PUBLIC`。工具、桥接环境和下载均在 E 盘。

## 启动及连接

- Ghidra GUI：运行 `E:\Program\ReverseEngineering\Ghidra\Start-Ghidra.cmd`。
- x64dbg：运行 `E:\Program\ReverseEngineering\x64dbg\Start-x64dbg.cmd`。启动后插件自动监听 `127.0.0.1:27042`。x32dbg 入口为同目录 `Start-x32dbg.cmd`；勿同时开启两种位数并争用同一 MCP 端口。
- Codex 的 `ghidra` stdio 入口为 `E:\Program\MCP\ghidra-mcp\launcher.py`。连接时检查并按需启动 headless 后端，只监听 `127.0.0.1:8089`，Java 堆上限 2 GiB。桥接退出后后端继续运行，便于保留分析状态；未注册开机服务。日志为 `logs/headless.log`。
- Codex 的 `x64dbg` stdio 入口直接使用现有 node.exe 启动固定安装的 `dist/index.js`，没有每次运行 npx 在线下载。调试器需要先启动；不会自动附加游戏。请求超时配置为 15000ms。
- 配置写入 `C:\Users\RedAndBlack\.codex\config.toml`，保留原有 `wrath-analysis` 和其他配置。安装前副本在 `E:\Program\MCP\backups\codex-config-native-tools-*.toml`。
- 当前会话的工具列表如果未刷新，重开 Codex 会话或应用后再检查；独立 SDK 握手成功与当前聊天已出现工具是两件事。

Ghidra headless 和 GUI 是独立分析上下文。headless 成功不表示 GUI 中已自动启用插件，也不表示已导入游戏原生 DLL。GUI 插件文件已放入安装目录 `Ghidra/Extensions/GhidraMCP`，使用 GUI 时可在插件管理器启用；本轮验证的是 headless 模式。

## 本轮验证结果

- 六个下载资产均核对 GitHub release API 给出的 SHA-256，来源、版本、哈希与目标目录记录在 `analysis/native-tools-downloads.json`。
- 真实 MCP stdio 初始化和工具发现成功：Ghidra 222 个工具，x64dbg 23 个工具。数量只表示本轮实际目录，不表示逐项验证全部功能。
- Ghidra 通过 MCP 导入自建 x64 PE、执行自动分析、读取元数据，并反编译导出函数 `ArithmeticProbe`，结果为 `return param_1 * 3 + 7;`。
- x64dbg 通过 MCP 加载同一测试 EXE、查询模块、读取 `cip`，然后停止该测试调试会话。未附加其他进程。
- 两个监听地址均为 IPv4 loopback；没有新增防火墙规则或远程监听。

完整协议结果：`analysis/ghidra-mcp-verification.json`、`analysis/x64dbg-mcp-verification.json`。测试入口为 `scripts/test_native_mcp.py --exercise`；运行前须先打开空闲 x64dbg，并已编译对应测试目标。省略 `--exercise` 只验证目录和调试器状态。测试报告可含实际运行地址及 PID，不能当作游戏运行测量。

复测命令：

```powershell
& 'E:\Program\MCP\ghidra-mcp\.venv\Scripts\python.exe' .\scripts\test_native_mcp.py --exercise
```

移除 Codex 注册可用 `codex mcp remove ghidra` 和 `codex mcp remove x64dbg`。仅移除注册不会删除工具或分析项目。关闭后端前先保存分析工作；调试结束后通过 x64dbg 的正常退出方式关闭调试器。

## 上游来源

- [Ghidra 官方 12.1.2 发布](https://github.com/NationalSecurityAgency/ghidra/releases/tag/Ghidra_12.1.2_build)
- [x64dbg 官方快照](https://github.com/x64dbg/x64dbg/releases/tag/2026.05.27)
- [社区 Ghidra MCP 6.0.0](https://github.com/bethington/ghidra-mcp/releases/tag/v6.0.0)
- [社区 x64dbg MCP 2.3.0](https://github.com/bromoket/x64dbg_mcp/releases/tag/v2.3.0)

MCP 桥接属于社区项目，不是 Ghidra / x64dbg 官方内置服务。本次原生工具验证不替代现有 Mono/C# 分析，也不构成任何游戏优化收益验证。
