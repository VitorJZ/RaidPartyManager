# 设计决策记录

后续改动如影响数据归属、公共接口或对战结果，在这里记录日期、决定、原因与验收场景。

| 日期 | 决定 | 原因 | 验收 |
|---|---|---|---|
| 2026-11-16 | 首期只交付一场可重置的演练，场景组装五个模块 | 先验证战前管理与战中自主发挥，并让程序、内容与界面可以并行 | `BuiltSceneSupportsPreparationPlaybackLootAndReset` |
| 2026-11-16 | AI 按性格选取模拟器提供的合法候选，战术保留策略入口 | 保留 FM 式的经理体验，让未来的战术功能能单独实现 | `BattleTests` 的六种性格与注入策略用例 |
| 2026-11-16 | 用整数刻驱动模拟、按稳定顺序输出结果 | 相同配置可以复现，方便比较两套站位并定位差异 | `SameInputsProduceSameEventsWithStableSideAndPositionOrdering` |
| 2026-11-16 | 首期采用减法伤害、固定技能冷却和简单的护盾规则 | 核心结果可解释，容易复核目标、防御与冷却各自的影响 | 伤害、护盾和技能等待用例 |
| 2026-11-16 | 装备归背包，槽位引用实例 ID。胜利发装备，角色不通过战斗获得成长 | 装备可替换和转交，也能与将来的角色进化独立演进 | `PartyRunTests` 的配装转移、结算与形态切换用例 |
| 2026-11-16 | 战斗站位卡显示上方生命条和下方状态栏。护盾以结算后的剩余量为准 | 玩家能从站位直接读到承伤与护盾变化 | `BuiltSceneSupportsPreparationPlaybackLootAndReset` 验证生命扣减、护盾消耗和倒下 |
| 2026-11-16 | 角色编辑页、战斗站位和装备缩略图共用职业边框，立绘沿较小的内切角裁切 | 保留职业辨识度并展示更多立绘 | 两项 Play Mode 场景用例与 Unity 渲染检查 |
| 2026-11-16 | 角色装备在原页弹窗中操作。4×4 背包按格显示未装备物品，长按后可拖拽并互换 | 保留队伍总览上下文，通过原背包格反馈交换结果 | `PartyEditorOpensProfileAndSharedBackpackOutsideCombat` |
| 2026-11-17 | Buff 图标沿用策划表的编号和名称，方形原图由 Git LFS 管理，Unity 导入为 256 × 256 的单张 Sprite | 便于在策划表、Obsidian 图鉴和 Unity 之间查找同一状态，保留高分辨率原图供后续使用 | Unity 批处理验证 16 张 Sprite 的尺寸，按 SHA-256 核对 Obsidian 与 Unity 原图 |

| 2026-11-18 | 弥雾码头改为十关含 Boss，三处分叉的每条路径保留五次普通／精英战及三次服务 | 最新需求替代 7+1 布局；九个非 Boss 槽位只容纳一个额外事件，因此按选择动态限制事件与精英数量 | `AdventureTests.EveryForkPathPreservesTenNodesFiveFightsAndServiceQuotas` |
| 2026-11-18 | 事件检查与旧演练各用独立队伍；事件、城镇、营地、商店、战斗之间共用一份试游状态 | 方便自由检查全部事件，同时保留旧演练配装和试游中的当前生命、金币与装备 | `AdventureSceneTests` |
| 2026-11-18 | 宝箱伙伴按三个后续关卡归队，名额与装备保留；营地恢复存活在场队员最大生命的 30% | 用户确认推进单位和恢复量，避免暂离伙伴被第五名队员挤占 | 宝箱、招募名额与营地用例 |
| 2026-11-18 | 技能特效按技能书 ID 和实际结算目标播放，蓄势群攻保留技能书 ID 到下一击 | 区分共用结算规则的万箭齐发、星陨术与横扫千军，使动画跟随实际目标 | `WorkbookUpdateTests` 的图集、范围与延后触发用例，`LearnedSkillSpendsAPurpleEnergyCellAndSettlementRefillsIt` 的实战换帧检查 |
| 2026-11-19 | 墓园新增 23 普通、5 精英、2 Boss，采用可选的纯数据行为配置 | 允许按权重、目标、伤害和状态组合新敌人，原有码头行为保持兼容 | `GraveyardHasExactlyThirtyDistinctEnemiesWithCorrectRanksAndSlotBudgets`、`EveryAuthoredActionCanBeSelectedAndResolvedUsingItsRealProfile` |
| 2026-11-19 | 墓园重击先预警，下次可行动时重选目标并释放；半血阶段永久增加 1 攻击修正 | 高强度攻击保留换位、治疗和护盾应对窗口，阶段变化可追溯 | `ChargedAttackTakesTwoActionsAndReevaluatesRearTargetAfterASwap`、`BossPhaseTriggersOnceAtHalfHealthAndAddsOneAttackPower` |
| 2026-11-19 | 墓园通过独立场景试玩，敌阵按遭遇资源分别缓存；正式路线暂沿用码头 | 避免切换演练后混入其他区域敌人，不扩展尚未确认的墓园关卡路线 | `MainMenuOpensGraveyardAndSwitchingRegionsKeepsTheirEnemyPoolsSeparate` |
| 2026-11-19 | 原共享 Excel 不可访问时输出独立墓园增补表并注明未合并 | 提交完整可检查的数据与插图，避免误称已写入原在线图鉴 | 工作簿 30 条敌人、62 条行动、30 个概率合计和 2200 场试算核对 |
| 2026-11-19 | 取得用户提供的本地图鉴后，在敌人表末尾追加墓园，并保留原 8 个工作表及原生音效表 | 合并为 53 名敌人的完整图鉴；保留旧数据与格式，避免导出时丢失未修改的原生内容 | `MergeGraveyardWorkbook.mjs --verify`、`PreserveGraveyardWorkbook.py`，记录见 `WhisperingGraveyardValidation.json` |
| 2026-11-21 | 正式码头按等级抽预设敌阵，演练可载入码头和墓园的普通、精英、首领预设 | 保留有意设计的前后排和随从搭配，复用墓园已测阵容，沿用人数缩放与升阶试炼既有规则 | `DockEncountersSelectAuthoredRanksAndKeepWorkbookStats`、`DockFormationsScaleToAStartingPair`、`LoadingAGraveyardPresetUsesTheWholeFormationInBattle` |
| 2026-11-21 | 编辑页右上角使用「返回」，移除编辑页内直达主菜单的按钮 | 保持同一编辑页的退出方向，沿原路径回到营地、商店、地图或战前画面 | `EquipmentPersistsAcrossMainMenuAndLeavingABattle` |
| 2026-11-21 | 技能书可覆盖角色已有技能 | 消耗新书并遗忘旧技能，遵守职业、槽位和技能去重限制 | `SkillBooksCanOverwriteKnownSkillsAndTheNewSkillsSurviveEvolution`、`BookmarksSwitchPagesAndCompatibleBooksCanReplaceLearnedSkills` |
| 2026-11-21 | 元素石英转换后播放实际命中元素的特效 | 保留原技能名称，命中特效、飘字与防御结算使用同一元素 | `BlazingFireUsesTheElementalQuartzDamageRulesAndImpactArt` |
| 2026-11-21 | 每回合一名可行动的己方队员先手，其余双方混排 | 保留己方开局优势，允许敌人在其他队员之间行动 | `SeededRoundsReplayWithAnAlliedOpenerAndMixedTurns`、`AStunnedAllyDoesNotTakeTheOpeningTurn` |
| 2026-11-21 | 正式讨伐的随机事件跨楼层去重，遭遇战可重复 | 让已出现的事件退出抽选，保留可重复的保底事件与存档续玩 | `RandomEventsAreRememberedAfterDecliningAndResumingOnAnotherFloor`、`ExhaustedEventPoolStillRollsAmbushWithoutMarkingItSeen` |
| 2026-11-26 | ElevenLabs 的 128 条录音追加到现有音效银行，按区域、舞台动作和战斗事件播放 | 复用环境淡入淡出和音量控制，保留原素材索引。铁壁拦截用只读战斗事件标记，追加攻击沿用 `EquipmentVisual` | `ImportedClipsMatchManifestAndKeepLegacyBankSlots`、`MainMenuAndStandaloneRegionsPlayTheirOwnAmbience` |
| 2026-11-26 | 装备和技能书在按住后的首次指针移动时启动拖拽 | 去掉 150 毫秒等待，快速移动出原格也可开始拖拽，原地点按继续保留原物品 | `PartyEditorOpensProfileAndSharedBackpackOutsideCombat`、`BookmarksSwitchPagesAndCompatibleBooksCanReplaceLearnedSkills` |
| 2026-11-26 | 通关终幕按舞台角色、幕布、「感谢游玩」艺术字的顺序显示 | 合拢的幕布遮住角色，艺术字位于独立界面层，保留金币及薪资摘要和 5 秒停留 | `CapitalBossRewardsLeadToFiveSecondsBehindTheClosedCurtain` |
