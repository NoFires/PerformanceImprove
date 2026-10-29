# Wrath Lifecycle Fix 0.1.0 试验版

**当前发行已合并并改名为 [PerformanceImprove 0.2.0](performance-improve.md)**。旧 WrathLifecycleFix 安装于2026-10-21迁出游戏 Mods 并备份，订阅清理现为合并版的独立功能开关。下文保留0.1.0的机制与历史安装记录，不表示仍应同时安装旧版。

这是本工作区第一个改变运行行为的修复 Mod，与只采样的 WrathPerformanceLab 分开安装、分开关闭。目标是升级/洗点结束后，释放控制器自己创建却未保存的 LateUpdate 订阅。**它不是三项性能问题的完整修复，也尚未完成游戏内 A/B 验证。**

## 根据什么实施

2026-10-20 洗点操作中，Fabric 订阅链可达的 Stopped 控制器从 4 增至 18，之后切换眷泽城仍为 18。静态代码中，LevelUpController.UpdateCommand getter 丢弃 Subscribe 返回值，Stop 没有相应取消。具体数据见 `docs/runtime-respec-test-20261020.md`。

本补丁保留原来的源、回调、命令和订阅建立过程，仅在 Subscribe 返回后保存原句柄；Stop 的原方法及回调执行结束、且 Stopped=true 时才 Dispose 该句柄。取消由游戏自带 UniRx/Owlcat 实现完成，只移除相应 observer/node。

不会遍历删除所有旧角色、清空 Fabric、释放其他 UI 的订阅、强制 GC、卸载资源、改写角色规则或保存文件。未实现法术书/进展树提速、VA2 设置清理或眷泽城加载优化。

## 安装及开关

包：`dist/WrathLifecycleFix-0.1.0.zip`。包含 WrathLifecycleFix/ 下 Info.json、WrathLifecycleFix.dll、README.md，可由 UMM 安装，或退出游戏后将此文件夹放到游戏 Mods/。沿用现有 UMM 0.32.4/Harmony，不分发游戏 DLL 或替换运行库。

**2026-10-21 00:14（香港时间）已按用户指示安装到游戏 Mods，并在 UMM 配置中设为启用；未启动游戏、未完成游戏内验证。** 保留已装诊断版 0.2.0。安装前备份262个配置文件，核对280个已有 DLL 均未改变；安装记录与回退路径见 `analysis/installations/20261021-001418-lifecycle-fix/installation.json`。安装包内 README 保留打包时的状态，当前部署状态以本说明及安装记录为准。

UMM 的本 Mod 开关控制修复。建议在启动时启用，再进入升级/洗点界面。关闭时移除自己的 Harmony 补丁并丢弃追踪元数据，活动界面的现有通知保持原行为；已停止对象的历史订阅不会重新建立。重新开启只能保证之后建立并捕获的订阅会被清理，不补抓之前已丢弃的句柄，因此比较测试应重新启动游戏。

当前严格限制 Assembly-CSharp SHA-256：

```text
2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953
```

对应已分析的本机 2.7.0x DLL。哈希不同、Stopped 字段不符、getter 签名不符或不再只有一个预期的 UniRx.Subscribe<Unit> 后接 pop 时，不安装此修复。之后有其他 Mod 改写该 getter、导致形状不符时，也会保留原 IL 并记录无法捕获，不猜测其他订阅的所有权。这个匹配保护不是完整 Mod 兼容性证明。

## 生命周期边界

- 活动期间仍按原 LateUpdate 时机转发；不会立刻执行所有更新。
- Stop 的回调仍可产生原有通知，在外层停止回调完成后再释放句柄；同一控制器重入 Stop 也延后到外层结束。
- Stop 在设置 Stopped 之前抛错时保持原订阅；成功重试后仍能清理。
- 设置 Stopped 后其他代码抛错时，原异常继续传播，已结束的订阅所有权仍被释放。
- Stop 之后首次访问 getter 时，新句柄立即释放；在 Stop 回调内部首次访问则延后到该回调完成。
- 不 Dispose 公共 ReactiveCommand 或其他消费者拥有的中间 ReactiveProperty。它们可能有独立生命周期问题，不在该补丁范围。
- 用 ConditionalWeakTable 保存所有权，避免 Dictionary<object,...> 自己成为强根；停止后移除记录。UniRx 取消过程抛错时记录失败并继续处理其他句柄，不把清理异常替换为游戏原异常。

## 日志和验收

`Mods/WrathLifecycleFix/Logs/<会话>.csv`，每 15 秒记录累计 captured_total、disposed_total、dispose_failures、outstanding_tracked、tracking_dropped_on_disable、getter_capture_installed。每个文件最多 2 MiB，旧会话不会自动删除。

outstanding_tracked 是捕获/停止释放的事件余额，不是实际存活订阅数；没有 Stop 的对象若自行被回收，事件余额不会自动扣除。关闭 Mod 丢弃追踪也单独记账，不能伪装成成功 Dispose。日志文件失败时停止该统计，不改变已安装修复的行为。

建议下一轮与原测试同存档、同 Mod 配置比较，由用户操作：

1. 新启动游戏时启用修复和诊断，检查 UMM 加载及 getter_capture_installed=true。
2. 进入洗点/升级，切换职业、技能和专长页面，进行与之前相近的一轮升级，确认预览、撤销、取消和提交仍正常。
3. 每次关闭界面后检查 Fabric 中 Stopped 控制器是否回到附近基线，避免原来每轮累积；dispose_failures 应为零。
4. 比较修复统计中捕获/释放变化与 Fabric 根的变化，不能只用弱引用或 Working Set 单列判断成功。继续记录长时间内存、保存/载入与切图表现。

当前预期收益是减少该条保留链及其每帧遍历负担，**尚无已测的 FPS、内存节省量、加载提速或避免闪退保证**。

## 已做验证

`tests/WrathLifecycleFix.Checks` 在独立 .NET Framework 进程完成 **32 项检查，全部通过，编译零警告零错误**：本机真实游戏 getter IL/版本保护；真实 Harmony 改写后的缓存身份和通知；Stop 回调、失败、重入、停用/卸载、异常保持；弱键所有权；以及游戏自带 Owlcat LateUpdate operator 在取消后移除自己的链表节点、保留其他节点。

实际 Owlcat 算子测试仅将单例读取重定向到托管链表测试壳，不运行 Unity 原生构造、对象搜索或游戏场景。它能验证托管取消路径，不能代替 Unity Mono、完整 Mod 组合及实际操作的测试。结果在 `analysis/lifecycle-fix-checks.txt`。

构建：`scripts/build-lifecycle-fix.ps1`；源码：`src/WrathLifecycleFix/`。工具与缓存继续使用既有 E:\Program 环境。
