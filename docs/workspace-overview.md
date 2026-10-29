# 正义之怒性能 Mod：逆向工作区

当前项目目录：`E:\Program\Development\WrathPerformance`。所有开发、分析与备份使用此目录；[迁移记录](docs/workspace-migration.md)。

首轮静态逆向已完成，分析对象是本机 `2.7.0x` 安装。当前交付包括反编译源码、程序集/Mod 哈希清单、程序结构说明、性能调查方案，以及已注册并实测可调用的 Codex MCP。

当前优化 Mod 为 **PerformanceImprove 0.4.1**：升级订阅清理、眷泽城 NPC 模型按需加载、玩家已成交可堆叠卖出物品清理、普通区域切换的重复资源清理跳过，四个优化开关独立；另带默认开启的**会话诊断日志**（一份 JSONL，可直接发给作者分析）和默认关闭的实验开关 `SpellbookCopyFast`。单 DLL 已通过206项独立检查；0.4.1 包已构建但**尚未安装**，游戏内当前仍是 0.4.0。原 WrathPerformanceLab 已并入并移出游戏目录。商人原生库存不扫描，独特/命名装备和任务道具保留。加载清理的预期收益与 A/B 方法见 [区域加载清理](docs/load-cleanup.md)，尚未进行游戏内验证。[使用说明](docs/performance-improve.md)；[诊断日志与测试方法](docs/diagnostics-log.md)；[卖出清理规则](docs/merchant-sale-cleanup.md)；[安装与备份记录](analysis/performance-improve-verification.json)。旧版安装包与报告保留作历史记录。

- [程序结构与源码入口](docs/architecture.md)
- [性能候选点、已有补丁与测量方案](docs/performance-plan.md)
- [工具安装、MCP 与复现方式](docs/environment.md)
- [Ghidra、x64dbg 与原生分析 MCP](docs/native-tools.md)
- [针对升级、眷泽城加载和长时间内存增长的调查](docs/symptom-investigation.md)
- [WrathPerformanceLab 诊断 Mod 使用说明](docs/diagnostic-mod.md)
- [全面深入分析：30 项机制、保留链与反证](docs/deep-analysis.md)
- [深入分析源码证据索引](docs/evidence-index.md)
- [2026-10-20 洗点升级运行实测](docs/runtime-respec-test-20261020.md)
- [2026-10-20 大地图进入眷泽城运行实测](docs/runtime-drezen-test-20261020.md)
- [优化实施状态与解决方向](docs/optimization-status.md)
- [WrathLifecycleFix 订阅清理试验补丁](docs/lifecycle-fix.md)
- [PerformanceImprove 合并版](docs/performance-improve.md)
- [区域加载中的重复资源清理（0.4.1）](docs/load-cleanup.md)
- [眷泽城 NPC 模型按需加载试验版](docs/deferred-npc-views.md)
- [下一阶段优化方案（开卡/升级优先）](docs/optimization-plan.md)
- [多 Mod 启动进入主菜单耗时分析（本轮实测）](docs/mod-startup-analysis.md)
- [诊断日志：格式、采集步骤与分析](docs/diagnostics-log.md)
- [程序集及 Mod 清单](analysis/inventory.json)
- [命名空间和程序集依赖统计](analysis/structure.json)
- [124 条控制器注册记录](analysis/controller-registrations.json)
- [历史启动计时摘录](analysis/historical-log-summary.json)
- [MCP 集成验证结果](analysis/mcp-verification.json)

`analysis/decompiled/` 首轮保存八个程序集的 13,739 个 C# 文件；进一步调查后已覆盖 51 个 Mod 主程序集及八个游戏/运行库程序集，共 59 个目录、28,232 个 C# 文件，其中游戏核心程序集有 10,150 个。该目录已加入 `.gitignore`，供本机分析使用。反编译输出保留算法与调用关系，但不等同于原始源码，也不保证可以直接重编译整个游戏。

WrathPerformanceLab **0.2.0** 是独立诊断 Mod，已安装并用于两次用户操作的运行记录；31项独立检查及已知采样限制见其说明。它覆盖升级/UI 耗时、加载阶段、Fabric、预览、Atlas/DXT 与 VA2 指标；进程内存采用外部采样补足。合并优化版与诊断版各有用途，不能把历史原 Mod 组合的记录当作 PerformanceImprove 的效果验证。
