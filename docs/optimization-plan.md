# 下一阶段方案：从逆向证据到可交付的优化

2026-10-22。本文只做两件事：把已经能证明的机制写清楚，给出一个不靠猜测的实施方案。所有数字都标注来源——运行测量、历史记录或静态推断，三者不混用。

## 1. 结论先行

1. **开卡/升级卡顿的主因已定位到具体方法**：每次预览重算都会整体复制角色，`CopySpellbook` 平均 291.5 ms、最大 804.8 ms，占预览复制总时长 78.5%（25 次共 7.29 s）。这不是猜测，是 2026-10-20 的诊断计时。
2. **其中至少一半是重复劳动，且能从代码证明**：`AddBaseLevel` 已经按"可知全部法术"的规则学了一遍，紧接着 `CopySpellbook` 又把 `GetAllKnownSpells()` 里的每一条重新 `AddKnown` 一次；而 `AddKnown` 对"已存在且非临时"的法术会把同一个 `AbilityData` 再 `Add` 进列表。
3. **角色面板本身的刷新是第二个独立热点**：`UnitProgressionVM.RefreshData` 每次全量销毁并重建 VM 树，96 次共 2.01 s，单次峰值 1.308 s。
4. **眷泽城的 48.8 s 目前只分解到边界**：再往下必须按具体方法打点。现有探针显示 1552 个 `LevelUpController` 只花 14.9 ms，说明"单位构建"不是那段 22.9 s 的主体——不能把 NPC 延迟加载当成已证实的加载优化。
5. **游戏自带的 `ProfileScope` 在零售版是空实现**（`Application.isEditor || BuildModeUtility.IsDevelopment` 才生效），所以任何"读游戏 profiler scope"的方案都拿不到数据，必须挂真实方法。这条直接决定了 Phase 0 的做法。

## 2. 逆向发现（可核对到源码）

路径均相对 `analysis/decompiled/Assembly-CSharp/`。

| # | 机制 | 位置 | 判定 |
|---|---|---|---|
| 1 | 预览单元每次重算都整体重建 | `Kingmaker.UnitLogic.Class.LevelUp/LevelUpController.cs:497` `UpdatePreview()`：`Preview.Dispose()` 后 `RequestPreview()` | 静态确认；运行计时 11 次 4.45 s |
| 2 | 复制走 `CopyInternal` → `CopySpellbook` | `Kingmaker.UnitLogic/UnitHelper.cs:99,126,193` | 静态确认 |
| 3 | 复制法术书时先逐级 `AddBaseLevel`，再对 `GetAllKnownSpells()` 逐条 `AddKnown` | `UnitHelper.cs:207-224` | 静态确认 |
| 4 | `AddBaseLevel` 在 `Blueprint.AllSpellsKnown` 时会真的把该等级全部法术学进列表 | `Kingmaker.UnitLogic/Spellbook.cs:276-304` | 静态确认 |
| 5 | `AddKnown` 对"已存在且非临时"条目会把同一个 `AbilityData` 再加进 `m_KnownSpells[level]`（重复条目） | `Spellbook.cs:361-369` | 静态确认 |
| 6 | `AddKnown` 每次都做：线性找已存在条目、写 `m_KnownSpellLevels`、在 `m_SpecialLists` 里线性找列表、`EventBus.RaiseEvent(ILearnSpellHandler)` | `Spellbook.cs:361-385` | 静态确认 |
| 7 | 游戏内 `ILearnSpellHandler` 唯一订阅者是神秘神学士合书 | `Kingmaker.Designers.Mechanics.Buffs/MysticTheurgeCombinedSpells.cs:22` | 静态确认；Mod 可能另有订阅，须运行时计数 |
| 8 | `Commit()` 只把计划应用到真实单位，预览被 `Dispose` | `LevelUpController.cs:1092,1106` | 静态确认 |
| 9 | 49 个存档里 16 个法术书对象 `m_KnownSpells` 重复条目为 0 | 本轮 `scripts/inspect_save_spellbooks.py` 扫描 Auto_83.zks（备份副本） | 运行测量（只读） |
| 10 | `ProfileScope.New` 在非开发版返回 null，作用域不做任何计时 | `Kingmaker.Utility/ProfileScope.cs:25-43` | 静态确认 |
| 11 | 法术列表按等级直接索引，`GetSpells(level)` 很便宜 | `Kingmaker.Blueprints.Classes.Spells/BlueprintSpellList.cs:67`，`SpellLevelList.cs:37` | 静态确认 |

第 9 条要和第 8 条一起读：重复条目只存在于"用完就丢"的预览单元里，所以不会污染存档——这既是安全性的依据，也说明 **A2（跳过重复 AddKnown）只影响预览内存与耗时，不影响存盘数据**；但如果别的 Mod 用 `Copy(preview:false)` 替换真实单位，就要另外核对。

## 3. 成本模型

### 3.1 开卡/升级（2026-10-20 用户操作段，202.6 s）

| 方法 | 次数 | 合计 | 单次均值 | 单次峰值 |
|---|---:|---:|---:|---:|
| `UnitHelper.CreatePreview` | 25 | 9,280 ms | 371.2 ms | 891.3 ms |
| `UnitHelper.Copy` | 25 | 9,280 ms | 371.2 ms | 891.3 ms |
| `UnitHelper.CopySpellbook` | 25 | 7,287 ms | 291.5 ms | 804.8 ms |
| `LevelUpController.UpdatePreview` | 11 | 4,447 ms | 404.2 ms | 897.1 ms |
| `LevelUpController..ctor` | 39 | 4,911 ms | 125.9 ms | 852.1 ms |
| `UnitProgressionVM.RefreshData` | 96 | 2,013 ms | 21.0 ms | 1,308.2 ms |
| `CharGenFeatureSelectorStateVM.CreateFeaturesList` | 19 | 1,437 ms | 75.6 ms | 265.9 ms |

推论：打开一次升级/开卡界面至少付 1 次约 370 ms 的复制；每改一次选择再付一次。25 次/200 s 意味着用户几乎每一步都在等这个复制。**这是"开卡卡顿"最直接的可攻击点。**

### 3.2 进入眷泽城（2026-10-20 用户操作段）

流水线 48.812 s（与原生 GameLogFull 一致，差 0 ns 级）：

| 分项 | 秒 |
|---|---:|
| `LoadArea`（含下列各项） | 28.569 |
| 昼夜/光照环境 | 9.442 |
| 两次资源清理 | 6.142 |
| 纹理等待 | 1.661 |
| 自动存档 | 1.326 |
| 其他 | 1.673 |

关键节点：`Scene set matched` 之后仍用 22.918 s；`Set active scene: ..._Light` 到"等待光照探针结束"9.150 s。

现有方法级探针在这一段只覆盖到：`RequestBundle` 1566 次 / 1,386 ms，`LevelUpController.Start` 1552 次 / 14.9 ms，`MatchStateWithScene` 28 次 / 100 ms，`LoadSceneEntities` 27 次 / 96 ms，`CleanupLoadedCache` 2 次 / 10 ms。`LoadAreaCoroutine` 内部的大段逻辑（`Load Sound Banks`、`PostLoad area state`、`Move Characters`、`Turn On`、`Finish Loading State`、`Process area spawners`）**当前没有任何数据**，因为它们的计时全靠零售版失效的 `ProfileScope`。

## 4. 候选方案与可行性

评分：收益/风险/证据强度，均按"现在就能做"判断。

### A. 法术书复制提速（首选）

- **A1 特殊法术表索引**（行为等价）：把 `AddKnown` 里的 `m_SpecialLists.FirstItem(list => list.GetSpells(level).Contains(spell))` 换成按"法术 → 首个包含它的特殊表"预建索引，`AddSpecialList`/`RemoveSpecialList` 时失效重建。语义等价（保留"表在列表中的先后"），纯性能。
  - 证据强度：中（能证明是 O(表数×表长) 的重复扫描，但未测占比）
  - 风险：低
- **A2 跳过已学法术的重复 AddKnown**：`CopySpellbook` 第二段循环前先 `IsKnownOnLevel(blueprint, level)`，已会则跳过。对"全员可知"职业可省掉整套重复；对法师这类需要复制已学法术的职业不受影响（本来就没学过）。
  - 证据强度：高（重复调用可由代码路径与存档对比证明）
  - 风险：中低（改变预览单元里的重复条目；不影响存档，见 §2 第 8/9 条）
- **A3 批处理 ILearnSpellHandler**：把逐条事件合并成一次。
  - 风险高（订阅者语义变化），**本轮不做**，只保留在 0.5.x 待议。

### B. 预览复制整体

- **B1 复用预览单元**：只对变化部分重放动作。收益可能最大（25 次 → 几次），但要能回滚任意 `ILevelUpAction`，风险很高，**不做**。
- **B2 合并同一帧的多次 `UpdatePreview`**：调用方可能同步读取 `Preview`，会改变时序，中高风险，**暂不做**。

### C. 角色面板增量刷新

`UnitProgressionVM.RefreshData` 每次 `Dispose` + 重建 `ClassProgressionVM`/`ProgressionVM`。改成"职业与等级集合未变时复用 VM、只刷新数据"能削掉 1.3 s 峰值。
- 证据强度：高（子对象创建是主要成本）
- 风险：中（UI VM 生命周期、Mod 可能挂在这些 VM 上）
- 建议：Phase 2，先看 A 的效果。

### D. 眷泽城加载细分

- **D1 步骤探针**：在 `SceneLoader.LoadAreaCoroutine` 的各段插入真实计时（不是 `ProfileScope`），拿到 `Turn On`、`Process area spawners`、`PostLoad area state`、`Move Characters` 的占比。
- **D2 光照探针 9.15 s**：先数 `ReflectionProbe.RenderProbe` 调用与耗时，再决定是否做"降低刷新频率/分辨率"的可选项。**不做画质承诺。**
- **D3 两次清理 6.14 s**：先确认两次是否重复。

### E. NPC 延迟加载的验证

功能已实现未实测。它只值得保留在"降低峰值内存/走动长帧"的目标下；加载总时长能否下降必须由 D1 证明，不能用 48.8 s 反推。

### F. 明确不做

强制 GC、清空历史 VA2 设置、全局删资源缓存、跳过清理、把 `LoadArea` 一律换成异步——这些要么破坏规则语义，要么只是把成本挪到别处。

## 5. 路线图

### Phase 0：诊断（本周，不需要玩游戏）

交付 `WrathPerformanceLab 0.3.0`，只加探针不改行为：

| 探针 | 目的 |
|---|---|
| `CopySpellbook` 入口/出口 + 两段循环分段计时 | 分清 `AddBaseLevel` 段与 `GetAllKnownSpells` 段的占比 |
| 计数器：`AddKnown` 调用数、`AddSpecial` 数、每次复制新增重复条目数 | 验证 A2 的可省工作量 |
| `AddKnown` 内部：找条目 / 特表扫描 / 事件 三段计时 | 决定 A1 还是 A3 |
| `ILearnSpellHandler` 运行时订阅者数量与类型 | 判断 Mod 是否放大事件成本 |
| `UnitProgressionVM.RefreshData` 内 `ClassProgressionVM`/`ProgressionVM` 创建次数与耗时 | 量化 C |
| `SceneLoader` 各段真实计时、`ReflectionProbe.RenderProbe` 计数、两次 Cleanup 调用点 | 量化 D |

一次用户操作（3 分钟）即可同时覆盖开卡与进城。产出：`docs/attribution-<日期>.md`。

### Phase 1：0.4.0（A1 + A2，独立开关）

- 新增开关 `SpellbookCopyFast`（默认关，A/B 后决定默认值），内部两个子项 `SpecialListIndex`、`SkipKnownReadd`，同样可独立关闭。
- Harmony 点：`Spellbook.AddKnown`（前缀/后缀）、`Spellbook.AddSpecialList`/`RemoveSpecialList`（失效）、`UnitHelper.CopySpellbook`（层间计时与跳过判断）。
- 离线检查：现有 145 项 + 新增（索引等价性、特殊表顺序、跳过条件、`AllSpellsKnown` 与非 `AllSpellsKnown` 两条路径、开关切换不遗留状态）。
- 运行时保护：`Assembly-CSharp` SHA256 不符则整体拒绝初始化，与现有三个功能一致。

### Phase 2：按数据二选一

- 若 A 的 A/B 显示预览复制仍 > 500 ms → 做 C（UI 增量刷新）。
- 若开卡已可接受 → 转 D（加载细分）并把 E 的验证补上。

### 验收门槛（Phase 1）

| 指标 | 门槛 |
|---|---|
| 预览复制总时长 | 同操作同存档下下降 ≥ 30%（9.28 s → ≤ 6.5 s） |
| 单次预览复制 | 均值 ≤ 200 ms，峰值 ≤ 600 ms |
| 规则一致性 | 升级前后已学法术集合、法术等级、可记忆槽数、法术书数量完全一致；法师/牧师/奥能师各验一遍 |
| 存档 | 不新增重复条目；存档可读、可读回 |
| 稳定性 | 无新异常；DXT/GC 计数不回退 |

## 6. A/B 协议（下一轮，用户操作）

1. 备份当前 `Settings.xml` 与本次要用的存档。
2. 关闭 `SpellbookCopyFast`，进游戏做固定动作：打开角色面板 → 升一级 → 改两次选择 → 提交。用 `scripts/record_user_operations.py --pid <PID> --name ab-baseline --interval 1` 录制。
3. 保持游戏不退出，打开开关，重复同样动作，录制 `ab-fast`。
4. 各录完在 `current-recording.txt` 指向的目录写 `STOP`，等 `metadata.json` 出现 `end_utc` 再冻结。
5. 用 `scripts/analyze_user_recording.py` 出对比表；同时读 Lab 的 attribution 数据。

判定只认本轮同存档同操作的两段，不与 2026-10-20 的历史段比。

## 7. 风险与回退

- 所有新功能独立开关，默认值在 A/B 后确认；关闭即恢复原路径（不重建已改过的预览，下次重算自然回原样）。
- 包与 DLL 哈希、安装备份、回退命令沿用 `scripts/update-performance-improve.py` 与 `analysis/installations/` 的既有格式。
- 若 A2 在某种职业组合下出现"法术缺失/等级错位"，立即关闭 `SkipKnownReadd` 并回到 A1 单项；两者互不依赖。

## 8. 需要确认的

1. "开卡卡顿"主要是哪个界面：升级界面、新建角色、洗点、还是角色信息页？三者走的复制次数不同，A/B 的动作要按主要场景定。
2. Phase 0 的探针版是否直接覆盖安装 `WrathPerformanceLab`（现有 0.2.0），还是先只做离线构建、等你确认再装。
3. 是否同意把 A1/A2 的默认值先设为关，等这一轮 A/B 再决定。
