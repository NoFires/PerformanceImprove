# 洗点升级运行实测：2026-10-20

这轮把两个本体机制提升为优化优先项：**预览法术书的同步复制成本**，以及 **Stopped 升级控制器仍被 LateUpdate 静态根持有且随操作增加**。本轮使用现有 Mod 配置，不能把测量值当作原版基准，也没有据此确认 OOM 或崩溃原因。

## 测试范围与数据

- 游戏 2.7.0x，Unity 2020.3.48f1，进程 PID 31800；诊断版 0.2.0，UMM 0.32.4。现有 Mod 开关未为测试作调整。
- 用户接管时的画面为柯米丽雅洗点升级。额外被动录制边界是香港时间 **23:19:08.164—23:22:30.766，202.60 秒**；后台收到停止标记后在 23:22:34 完成落盘，总运行约 206 秒。
- 由用户操作；该段不自动点击、不提交选择、不主动 GC、不清理游戏对象。会话开始前已备份并校验 49 个存档。
- 41 个进程外资源样本，间隔约 5 秒；游戏内部计时按 15 秒聚合，新生命周期事件触发受限采样。
- 冻结数据：`analysis/runtime/20261020-230635/user-operation-recording/start/`、`end/`、`samples.csv`、`metadata.json`、`analysis.json`。
- 复现分析：`scripts/analyze_user_recording.py`。JSON 保存输入 CSV 的 SHA-256；不是从持续变化的游戏日志重新推测历史。

起止计数采用边界附近最近一次内部采样，存在数秒到约 15 秒时差。计时行属于聚合窗口，首尾可能覆盖少量边界外调用。下面的累计时间均为包含子调用的时间，**嵌套项不能相加**，也不是整个操作的点击到显示耗时。

## 同步耗时

| 方法 | 聚合调用数 | 累计包含耗时 | 单次最高 |
|---|---:|---:|---:|
| UnitHelper.CreatePreview | 25 | 9,280.32 ms | 891.29 ms |
| UnitHelper.CopySpellbook | 25 | 7,286.71 ms | 804.77 ms |
| LevelUpController.UpdatePreview | 11 | 4,446.66 ms | 897.13 ms |
| UnitProgressionVM.RefreshData | 96 | 2,013.01 ms | 1,308.19 ms |
| CharGenFeatureSelectorStateVM.CreateFeaturesList | 19 | 1,437.08 ms | 265.88 ms |
| CharGenFeatureSelectorStateVM.UpdateFeatureState | 85 | 1,426.17 ms | 262.34 ms |
| CharGenVM 构造 | 14 | 170.61 ms | 20.24 ms |
| LevelUpController.ApplyLevelUpActions | 26 | 146.34 ms | 28.75 ms |
| UnitHelper.CopyItems | 25 | 34.27 ms | 1.89 ms |
| UnitHelper.CopyFacts | 25 | 14.93 ms | 0.95 ms |

法术书复制占预览复制累计包含耗时的 **78.52%**。这里的 `CreatePreview` 与其内部 `Copy` 基本为同一段时间，没有将两者相加。进展树刷新存在超过一秒的单次尖峰，应与法术书路径分别处理；本轮重新打开的建卡 VM 构造本身较短。早先本次启动的首次建卡构造最高 428.01 ms，是之前的样本，不与该用户段混算。

**静态机制与待分解项：** 本体 `UnitHelper.CopySpellbook` 会 DemandSpellbook、逐级 AddBaseLevel/AddMythicLevel，再枚举原角色全部已知法术逐个 AddKnown。`Spellbook.LearnSpellsOnRaiseLevel` 对全表已知型法术书会在升级时遍历法术表；`AddKnown` 又线性查找当前等级列表，并可触发 ILearnSpellHandler。还存在“找到已有 AbilityData 后仍向列表 Add”的路径，值得检查实际重复数量与事件成本，但本轮没有逐项计数，不能据此断言它已造成了多少重复或耗时。

下一次分解应记录 CopySpellbook 内 AddBaseLevel、LearnSpellsOnRaiseLevel、AddKnown 的次数/耗时，原始条目数与按法术级别/蓝图区分的唯一数，以及学习事件处理成本。缓存、去重或批处理必须先核对法术学习/撤销/特殊法术表/神话合书和 Mod 回调语义，不直接跳过事件。

## 对象保留与设置增长

| 指标 | 开始附近 | 结束附近 | 变化 |
|---|---:|---:|---:|
| Fabric 受限订阅链可达的 Stopped 控制器 | 4 | 18 | +14 |
| Fabric 节点总数 | 22 | 26 | +4，过程中峰值 132 |
| 受跟踪控制器总数/弱引用存活数 | 442 | 479 | +37 |
| 预览弱引用存活数 | 31 | 55 | +24 |
| 已 IsDisposed 但弱引用仍存活的预览 | 30 | 55 | +25 |
| 观察到已回收的预览 | 0 | 0 | 未观察到回收 |
| GC.CollectionCount(2) | 219 | 225 | +6 |
| VA2 PerCharacter | 2,386 | 2,410 | +24 |
| VA2 与已观察预览 ID 匹配的设置 | 9 | 33 | +24 |
| VA2 CurrentFXs 键槽 | 0 | 0 | 此路径未出现装备键增长 |

最强证据是直接可见的链：

```text
LastValueOnLateUpdateObservableFabric.s_Instance
  → m_ObservablesToLateUpdate
  → observable.m_Observers
  → downstream observer / delegate target
  → LevelUpController（Stopped=true）
```

末次有 18 个不同 Stopped 控制器通过该链可达；当时该组仍存在，且从委托目标列表已看不到活跃 CharGen VM/View 目标。节点总数会随打开/关闭正常波动，不等同于唯一泄漏对象数。

本体 `UpdateCommand` getter 创建 LateUpdate 订阅后没有保存返回的 IDisposable；`Stop` 释放 Preview、设置 Stopped 并清除 Game.LevelUpController，但未释放这条订阅。运行观察与这条静态机制吻合。优先补丁方向是跟踪并释放**该控制器自己创建的订阅**，保留 Stop 回调顺序和仍在使用的 UI 行为；不能直接清空 Fabric 或所有全局订阅。

弱引用存活数用于佐证，未完成每个控制器/预览的完整保留根分析，不能把 479 个控制器都称为这条链上的对象，也不能把 55 个预览都归到同一根。该 Unity Mono 下三代 GC 计数同步，只能表述为计数增加了 6 次，不能套用桌面 .NET 的完整分代回收解释。诊断自身仅存弱引用/标量，未强制 GC；本机 .NET 的弱引用测试通过，但尚无本轮 Mono 内独立回收对照实验。

VA2 设置与已知预览 ID 的匹配增长同为 +24，支持临时预览进入持久设置表的线索。它没有证明这些设置占据多少存活内存，原有 2,386 条也不是可删除名单。CurrentFXs 为零使“本轮大量旧装备键堆积”降级，不排除其他功能触发该路径。对 VA2 的处理是附加优化，本体问题仍需单独验证。

## 内存、压缩和地图

| 指标 | 段首 | 段末 | 段内最高 |
|---|---:|---:|---:|
| 进程 Private Bytes | 7.52 GiB | 7.50 GiB | 7.87 GiB |
| Working Set | 4.90 GiB | 4.91 GiB | 5.05 GiB |
| 托管使用量估计 | 1.89 GiB | 1.63 GiB | 1.99 GiB |

托管内存多次回落，进程私有内存没有单向增长；该短段未复现 OOM 或闪退。它仍可同时包含持续保留的小部分对象，因此“内存回落”和“旧引用增加”并不矛盾，不能据此推出长时间运行安全或计算每小时泄漏率。

段内记录 42 个 DXT worker_end 和 42 个 before_clear，未见异常事件，最终 DXT/Atlas 队列均归零。已观察请求清理前年龄最高约 **0.994 秒**，后台工作持续时间最高约 **0.201 秒**；没有丢事件或追踪容量淘汰。DXT 字典 NativeArray 描述符仍可保留 22,369,620 字节长度，这不是实时原生分配，不当作泄漏量。

该段新增地图加载行数为 **0**，仍在 RE_KareliaForest；不包含大地图进入眷泽城，不能用它排除那条路线的加载瓶颈。会话末保存的 Player/GameLog 中未找到 OOM、GPU 回读失败或诊断停止标记；部分游戏日志包含历史内容，缺少标记并非完整崩溃审计。

![运行趋势](../analysis/runtime/20261020-230635/user-operation-recording/respec-trends.png)

## 诊断开销与已知限制

- 该段扩展采样平均约 2.95 ms、最高约 3.91 ms；这是采样主体计时，未涵盖所有补丁、最终 Flush 及 OS 调度开销。尚未做关诊断的同操作对照，不能给出完整开销比例。
- Unity Mono 的 `System.Diagnostics.Process` 返回 Private/Working Set 为 0，原 memory.csv 中这两列无效。上表全部采用进程外 psutil；不是零内存。
- EventBus 的 PooledDictionary 包装未解包，global_subscriber_slots/unit_subscriber_groups 为 -1，不代表零订阅。下次诊断版本需修正，不能从这两列推断全局泄漏。
- 所有已记录计时探针的异常计数为 0；这并不覆盖游戏里每个函数和所有被其他 Mod 截获的异常。
- 受限链扫描并非完整对象图；本段未耗尽时间/步骤预算，但有白名单/深度范围限制。没有原版或仅诊断 Mod 对照，具体耗时仍包含已装 Mod 的影响。

本轮额外被动录制与进程外采样均已停止，游戏保持运行，已加载的诊断 Mod 仍按自己的周期记录。未部署优化补丁，没有回滚用户的洗点升级操作。原始数据已冻结，后续游戏操作不会改写这份报告的输入。
