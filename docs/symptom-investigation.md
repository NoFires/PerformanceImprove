# 针对三个实际症状的调查

用户报告：升级或“开卡”明显卡顿；从大地图进眷泽城可能等几分钟；游玩久后内存增长、卡顿并可能闪退。将这三项作为当前优化优先目标，普通战斗每帧优化暂后置。

用户进一步确认：“开卡”包括打开已有角色面板，以及新建角色／洗点，两种操作都会卡。两条 UI 路径分别调查，并检查是否存在共同的数据重建或资源生命周期成本。

用户补充：无 Mod 的原版也会卡，安装 Mod 后更严重。因此把游戏本体的计算成本和生命周期作为主线，Mod 作为可能的放大因素单独测量；现有静态分析不能将全部卡顿归因于 VA2 或其他 Mod，也不能仅凭此补充断定原版闪退的原因。

## 升级、建卡和角色详情

已确认调用链：LevelUpController.UpdatePreview 在需要重算时 Dispose 旧预览，再 RequestPreview → UnitHelper.CreatePreview → Copy。CopyInternal 创建真空单位、复制 progression/stats/facts/spellbooks/proficiencies/items，可能重建 view，最后 TurnOn。重算后再 ApplyLevelup / ApplyLevelUpActions，并触发前后升级事件及 ReapplyFeaturesOnLevelUp。

另外，CharGenFeatureSelectorStateVM.CreateFeaturesList 会枚举可选专长并为全部结果建立 VM，之后还会遍历进行可用性和推荐状态刷新。大型职业、法术和专长 Mod 有可能放大这些路径，但不能凭 Mod 数量直接归因。

打开已有角色详情走 CharacterInfoVM → CreateVMs/CreateVM；进展页的 UnitProgressionVM.RefreshData 则先 Dispose 上批子 VM，再建立进展数据。这与升级预览复制是两条不同路径，所以诊断同时覆盖两者，不把普通角色面板假设为必然触发一次完整角色复制。

首选测量：预览复制分项耗时、每个 UI 操作对应的重建次数、专长列表和进展树刷新耗时。优化应优先消除冗余重建与重复计算，同时验证撤销、职业切换、神话选择、伙伴/宠物和 Mod 规则的缓存失效。

## 大地图到眷泽城

Game.LoadArea 排队的不止 SceneLoader.LoadAreaCoroutine，还包括旧实体卸载、资源清理、存档/区域状态恢复、AfterSceneLoaderFinishedArea、PreloadUnitResources、MatchTimeOfDay、可能的自动存档、纹理压缩等待、AreaLoadingComplete 等。

SceneLoader.LoadAreaCoroutine 内部还包含 UI 重载、声音包、静态/动态场景匹配、区域状态 Unstash/PostLoad、单位与地图物件预载、场景实体匹配和 TurnOn、导航网格。进度条停在某个百分比无法直接定位代码，尤其后续阶段还会重设进度。

诊断以实际 LoadingProcess 阶段墙钟时间分层：先找等待发生在哪一段，再细分同步热点。若存档实体恢复慢，应再检查区域数据量及回调；若资源预载慢，则记录实际加载 Bundle 数和磁盘等待；若长时间游玩后才慢，需要联动同一场景的内存趋势。

## 长时间卡顿和闪退

需要区分托管对象被保留、Unity 原生资源、AssetBundle/贴图增长、日志/异常洪泛，以及与内存无关的崩溃。普通 GC 不能释放仍被订阅/静态字段引用的对象，强制清理资源又可能造成重载和更严重的切图卡顿，因此不加入定时强制 GC 或强行销毁资源的补丁。

重点观察：反复进入相同场景或开关 UI 后，Private Bytes、Mono 已用量、Unity 已分配量是否持续阶梯式增长；同时检查全局订阅槽、单位订阅组、资源缓存/仍加载 Bundle 的趋势。诊断计数不能直接提供保留根，发现趋势后还需针对目标类型做生命周期追踪或进一步内存分析。

## 本轮历史日志证据

扫描 LocalLow 下 GameLogFull.txt、GameLogFullPrev.txt、Player.log、Player-prev.log，均为 2026-05-02 附近的旧文件。结构化结果在 `analysis/symptom-log-evidence.json`，由 `scripts/analyze_symptom_logs.py` 可复现。

- 两份完整日志大小分别为 114,017,867 和 115,197,626 字节，约 322,879 和 332,824 行。存在大量 Mod 启动输出与调用栈；文件大本身不能证明长时间运行存在日志泄漏。
- 最新完整日志记录到的最大 LoadAreaCoroutine 阶段约 5.76 秒，前次约 4.31 秒。单次清理约 2.5–3.1 秒；这些不是所有加载阶段总和，也不是用户描述的数分钟眷泽城复现。
- 两份 Player 日志和完整游戏日志中均未检出本脚本所查的明确 OOM/分配失败/Crash 标记。缺少标记不排除 OOM，需要下一次故障记录或崩溃转储。
- 最新完整日志有较长 FullScreenUi 开启到关闭的时间间隔。这可能只是用户停留在界面，不能当作游戏冻结时长。
- 旧日志可见重复 Mod 文本、进展树防循环警告，以及重复订阅提示。优先保留为线索，后续按实际操作复现来确定因果，暂不禁用 Mod 或静默屏蔽全部错误。

已实现并打包 `WrathPerformanceLab` 诊断版，详见 `docs/diagnostic-mod.md`。当前仍未实施改善性能的补丁，也未把诊断 Mod 部署到游戏；不把“编译/独立检查通过”当作“游戏内修复成功”。
