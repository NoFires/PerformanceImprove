# PerformanceImprove 0.5.1

**Targeted loading and lifecycle optimizations for Pathfinder: Wrath of the Righteous, with built-in diagnostics.**

PerformanceImprove is an experimental Unity Mod Manager + Harmony mod focused on area transitions, Drezen NPC models, level-up controller cleanup, and sold-item accumulation. Features have separate settings in the UMM panel.

## Features

| Feature | Default | What it does |
| --- | --- | --- |
| Level-up subscription cleanup | On | Captures the level-up controller subscription and disposes it after the controller stops. Does not force garbage collection. |
| Drezen NPC models on demand | On | Defers eligible distant NPC models in ordinary Drezen while retaining logical entities. Warms models near the party or camera; default party radius is 45 m. |
| Sold-item cleanup | On | Removes approved stackable items from the player's completed sale before they accumulate in merchant inventory. Existing merchant stock is left alone. **Removed items cannot be bought back.** |
| Duplicate area cleanup skip | On | Skips the second resource cleanup during ordinary area transitions. Save loading keeps both cleanups by default. |
| Faster character texture queue | On | Advances the character atlas queue several times per frame during texture-compression waits; default burst 4, clamped to 1鈥?. In-game performance still requires A/B testing. |
| Session diagnostics | On | Writes timings, preview-copy statistics, loading phases, memory samples and mod inventory to JSONL. |

Two experiments are **off by default**: `SpellbookCopyFast` skips already-known spell re-additions during preview copying; `SkipFirstCleanupOnSaveLoad` skips the first save-load cleanup only when the system memory gate (at least 12 GB) passes. These require controlled in-game testing. SpellbookCopyFast uses the diagnostics patch path, so keep Diagnostics enabled when testing it.

NPC safeguards cover party members, pets, special models, scripted access, dialogue, cutscenes and combat. The separate story-return Drezen map is excluded; this has not been verified against every story variant.

## Requirements and compatibility

- Windows installation of Pathfinder: Wrath of the Righteous with Unity Mod Manager installed.
- Current code targets the locally inspected **2.7.0x** game build. Compatibility guards use Assembly-CSharp SHA256 `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953`; do not assume other builds work.
- Remove the old standalone WrathLifecycleFix and WrathDeferredNpcViews installations before using this merged mod. Standalone WrathPerformanceLab diagnostics are also superseded.
- Compatibility with other mods, including Performance Fixes, has not been exhaustively tested.

## Installation and removal

1. Exit the game and back up your saves.
2. Install the packaged `PerformanceImprove` folder under the game's `Mods` directory, or install the release ZIP using UMM.
3. Start the game, verify version 0.5.1 in UMM and review its individual settings. Disable sold-item cleanup if you want to retain buyback access.
4. When upgrading, preserve `Mods/PerformanceImprove/Settings.xml` and `Logs`.

To remove the mod, exit the game and remove its folder. Removing it cannot restore items already discarded by sold-item cleanup; use a prior save if necessary. Restart between A/B runs.

## Logs and feedback

Diagnostics are saved to `Mods/PerformanceImprove/Logs/perf-*.jsonl` (default limit 32 MiB per session). Include the game/mod versions, enabled features, reproduction steps and relevant log when reporting a problem. Logs include the installed mod list; review them before sharing publicly. Individual lifecycle, NPC and merchant logs have separate limits.

## Validation and current limits

The existing build workflow runs six suites against the merged DLL: lifecycle (32), NPC views (51), integration (22), merchant (40), diagnostics (31), and load cleanup (69): **245 independent checks**. These cover managed behavior, actual game IL/signatures, patching and configuration, not the full Unity runtime.

Historical 0.4.0 game logs recorded 21 captured/released lifecycle subscriptions and 28 deferred/restored NPC models without reported failures in those counters. That is a limited observation, not proof of universal compatibility or a benchmark. No measured FPS increase or fixed percentage loading improvement is claimed. The 2026-10-30 save-load A/B (same save, six fresh processes) is recorded in [docs/runtime-ingame-20261030.md](docs/runtime-ingame-20261030.md): the texture window now activates but shows no measurable timing gain, and normal area transitions were not exercised.

**Texture-wait window:** the burst is active while `LoadingProcess.TickLoading` reports the `AwaitTextureCompression` iterator as the current stage. 0.5.0 wrapped the coroutine factory's return value, but a 2026-10-30 in-game run showed the loading manager kept the original iterator and the pumped counter stayed 0; patching the iterator's `MoveNext` is rejected by the runtime (`InvalidProgramException`). 0.5.1 reads the loading manager's current stage instead and checks that layout offline. In the 2026-10-30 in-game run (same save, fresh process per run) 0.5.0 pumped 0 calls while 0.5.1 pumped 903 (burst 4) and 2,170 (burst 8) calls with stage names unchanged. The stage took 4.63–5.19 s with the pump on and 4.78–5.21 s with it off, so the switch shows no measurable gain on save loads and should be treated as experimental. That load was dominated by SaveManager.LoadRoutine (86–147 s).

## Build from source

Use a .NET SDK capable of building net481 projects and .NET Framework 4.8.1 reference assemblies. Game, Unity, UMM and Harmony DLLs must come from your own installation; they are not included.

The projects currently contain local paths for `Managed` and NuGet caches. Adjust those properties for your machine (or pass `-p:Managed="<game>/Wrath_Data/Managed"` to direct dotnet builds). The packaging script also sets local cache directories; review it before running elsewhere.

```powershell
# Run from the repository root on the original development machine:
powershell -ExecutionPolicy Bypass -File scripts/build-performance-improve.ps1
```

The script builds, runs the six suites and produces `dist/PerformanceImprove-0.5.1.zip`. `analysis/` and `dist/` are generated/local directories excluded from source control. The script creates its local `analysis/` output directory when needed.

## Source layout

- `src/PerformanceImprove/`: UMM entry, feature switches, merchant cleanup, diagnostics and load optimizations.
- `src/WrathLifecycleFix/` and `src/WrathDeferredNpcViews/`: shared feature source linked into the merged DLL; old standalone entry points remain for historical builds.
- `src/WrathPerformanceLab/Telemetry.cs`: shared telemetry source.
- `tests/`: managed verification suites.
- `docs/`: feature rules and development analysis. Some documents describe historical versions and reference local evidence excluded from this repository.

Detailed rules: [mod guide](docs/performance-improve.md), [sold-item protections](docs/merchant-sale-cleanup.md), [load cleanup](docs/load-cleanup.md), [diagnostics](docs/diagnostics-log.md).

## Nexus description / 涓枃浠嬬粛

**鍚嶇О锛歅erformanceImprove 鈥?鍖哄煙鍔犺浇涓庣敓鍛藉懆鏈熶紭鍖?*

閽堝銆婂紑鎷撹€咃細姝ｄ箟涔嬫€掋€嬬殑鍖哄煙鍒囨崲銆佺湻娉藉煄 NPC 妯″瀷鍔犺浇鍙婂崌绾ф帶鍒跺櫒璁㈤槄娓呯悊鎻愪緵鍙崟鐙紑鍏崇殑浼樺寲锛屽苟鍐呯疆璇婃柇鏃ュ織锛屾柟渚垮畾浣嶅崱椤挎潵婧愩€?
- 鏅€氱湻娉藉煄鎸夎窛绂诲拰闀滃ご棰勭儹 NPC 妯″瀷锛屼繚鐣欓€昏緫瀹炰綋涓庡墽鎯呮仮澶嶅叆鍙ｃ€?- 鍗囩骇鎺у埗鍣ㄥ仠姝㈠悗閲婃斁鍏堕仐澶辩殑璁㈤槄锛屽噺灏戞棤鏁堢敓鍛藉懆鏈熸畫鐣欍€?- 鏅€氬尯鍩熷垏鎹㈣烦杩囩浜屾璧勬簮娓呯悊锛涜妗ｉ粯璁ょ淮鎸佸師鏈夋竻鐞嗘鏁般€?- 鍙嚜鍔ㄦ竻鐞嗙帺瀹跺凡缁忔垚浜ょ殑鍚堣鍙爢鍙犲崠鍑虹墿鍝侊紝淇濈暀浠诲姟銆佺嫭鐗逛笌鏃犳硶纭瀹夊叏鐨勭墿鍝併€?*娓呯悊鍚庢棤娉曞洖璐紝璇锋寜闇€鍏抽棴銆?*
- 鎻愪緵鍔犺浇銆佸崌绾ч瑙堝拰鍐呭瓨璇婃柇鏃ュ織銆傝创鍥鹃槦鍒楀湪鍔犺浇绛夊緟鏈熼棿姣忓抚澶氭帹杩涘嚑娆★紱鍗忕▼鎸傛帴闂宸蹭慨澶嶅苟閫氳繃鍥炲綊娴嬭瘯锛屽疄闄呮敹鐩婁粛闇€娓告垙鍐呴獙璇併€?
杩欐槸瀹為獙鎬?Mod銆傚綋鍓嶉拡瀵?2.7.0x 鏈満绋嬪簭闆嗛獙璇侊紝涓嶆壙璇哄浐瀹氬姞閫熸瘮渚嬨€佽В鍐虫墍鏈夊唴瀛樺闀挎垨闂€€銆?37 椤圭嫭绔嬫鏌ヤ笉鑳芥浛浠ｇ湡瀹炴父鎴忓強鍏?Mod 缁勫悎娴嬭瘯銆傝澶囦唤瀛樻。锛屽苟鍦?UMM 涓‘璁ら渶瑕佸紑鍚殑鍔熻兘銆?
浠嬬粛缁撴瀯鍙傝€?[ADDB 鐨?Performance Fixes](https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/797)锛氱畝浠嬨€佸姛鑳姐€佷緷璧栥€佸畨瑁呭拰鍏煎鎬с€傛湰椤圭洰鍏虫敞鐨勪紭鍖栬矾寰勪笉鍚岋紝鏈部鐢ㄨ Mod 鐨勬€ц兘鏁板瓧鎴栧垎鍙戝叾鏂囦欢銆?




