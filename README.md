# 讨伐队经理 · Raid Party Manager

四人小队 Roguelike 自动战斗游戏。玩家像球队经理一样挑选阵容、安排站位与装备。开战后，队员根据特性、局势和自己的性格行动。

## 运行首期原型

1. 克隆仓库后运行 `git lfs pull` 下载立绘、图标和技能特效图集，再用 Unity Hub 打开仓库根目录。编辑器版本为 **Unity 6000.6.9f1**。
2. 在 Unity 点击 Play 进入[主菜单](Assets/Scenes/MainMenu.unity)。选择「开始讨伐」后先从四名候选中选两人，再进入地图；已有存档时显示「继续讨伐」。也可打开「战斗」「低语墓园」「商店」「营地」或「事件选择」，在顶栏的设置中可返回主菜单。商店测试以 999 金币开始，将货架商品拖到收银台购买。营地显示当前队伍在篝火两侧休息的坐姿，可在「角色编辑」替换成员和站位。界面建议使用 16:9，例如 1280×720。Windows 演练界面优先使用系统微软雅黑显示中文。
3. 首次进入演练时，己方从 35 名角色中抽 4 名，敌方从该区域的普通敌人中抽 4 名，同侧不会抽到重复角色。码头共有 23 名敌人（17 普通、4 精英、2 Boss），低语墓园共有 30 名（23 普通、5 精英、2 Boss）。用「角色编辑」更换己方成员、站位或装备；要试打精英或 Boss，在设置中打开「敌方编辑」手动布阵。
4. 点顶栏的「开始战斗」。每回合己方先行动，敌方随后行动，两边各自重新随机排序。当前行动的动画播完后，下一名角色立即行动。角色从屏幕两侧按站位顺序入场。姓名显示在上方，红色生命条与状态图标位于角色下方并随回放更新。悬停状态图标可查看效果。右下方以羊皮纸显示战斗日志。战斗会自动推进。点击顶栏下方的「暂停」可在当前回合结束后停下。暂停时可点击「继续」，也可长按拖动一名存活队员与另一名存活队员交换站位，两人换位后自动继续。胜利弹窗中点击狂战士之印和金币图标领取，全部领完返回主菜单。「重置演练」时，未编辑的一方重抽阵容，编辑过的一方保留选定角色。同一次 Play 中，切换场景和重置演练都会保留配装和共享背包。停止后重新点击 Play 会恢复初始装备。

战前和败北结算画面的「角色编辑」可打开队伍总览。长按立绘约 0.3 秒可拖拽换位，也能拖到空位或移出站位。点按立绘打开角色面板和 4×4 分页背包。测试背包初始有 50 种装备各 4 件，技能书各一本。悬停查看效果，长按物品可拖到装备槽。

职业边框会随角色类型应用到战斗站位、装备缩略图和角色编辑页。查看[新版角色立绘总览](Docs/Previews/ExpressivePortraits.jpg)和[图鉴更新说明](Docs/WorkbookUpdate.md)。

「事件选择」可直接检查九种事件和七类节点：城镇、营地、商店、普通战、精英战、Boss 战、宝箱。事件检查与独立战斗演练使用各自的队伍。正式讨伐从下往上推进，地图只显示当前位置、上一站和下一步；起点之外每层有十站，包括三场必经战斗、一个宝箱、五处分叉和第十关的 Boss。需要跨次 Play 继续正式讨伐时，在顶栏设置选择「保存并退出」。参阅[事件与关卡说明](Docs/DockAdventure.md)。

[完整 Excel 图鉴](Docs/Bestiary-WithGraveyard.xlsx)已合并墓园内容，保留原 8 个工作表，共 53 名敌人。墓园的 30 名敌人包含 62 项行动和 30 张内嵌立绘；独立的[墓园增补表](Docs/WhisperingGraveyard.xlsx)另含难度试算。[图鉴维护说明](Docs/WorkbookMaintenance.md)记录数据来源、重建与校验步骤。两份 Excel 均由 Git LFS 管理。

## 测试

在 Unity 的 **Window → General → Test Runner** 中运行 Edit Mode 和 Play Mode 测试。命令行可分别运行：

```powershell
$unity = 'E:\ProgramFile\UnityEditor\6000.6.9f1\Editor\Unity.exe'
$project = (Get-Location).Path
New-Item -ItemType Directory -Path (Join-Path $project 'Logs') -Force | Out-Null
foreach ($mode in 'editmode', 'playmode') {
    $report = Join-Path $project "Logs\$mode.xml"
    $log = Join-Path $project "Logs\$mode.log"
    $unityArgs = @('-batchmode', '-projectPath', ('"{0}"' -f $project), '-runTests',
        '-testPlatform', $mode, '-testResults', ('"{0}"' -f $report), '-logFile', ('"{0}"' -f $log))
    if ($mode -eq 'editmode') { $unityArgs += '-nographics' }
    $process = Start-Process -FilePath $unity -ArgumentList $unityArgs -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "$mode failed. See $report and $log." }
}
```

从仓库根目录执行，按需修改编辑器路径。退出正在使用该项目的编辑器，或者对独立副本运行命令。Play Mode 测试会加载实际的演练场景并通过 UI Toolkit 验证布阵、回放、悬停与拖拽。

墓园内容的验收记录见 [WhisperingGraveyardValidation.json](Docs/Validation/WhisperingGraveyardValidation.json)，包含测试结果、资源数量、图鉴核对和文件哈希。正式讨伐路线目前仍使用码头，墓园通过独立场景试玩。

2026-11-20 数值测试覆盖 114,722 次战斗执行、600 条码头路线和两区域全部 53 名敌人。报告记录调参优先级、对照条件与样本边界。[Markdown 报告](Docs/Balance/2026-11-20.md) · [Excel 明细](Docs/Balance/2026-11-20.xlsx)

## 开发入口

- [战斗界面与状态栏](Docs/BattleUI.md)
- [20 种技能特效的动画预览与制作说明](Docs/SkillEffects.md)
- [码头商店](Docs/DockShop.md)
- [营地场景与 35 名角色坐姿](Docs/Camp.md)
- [弥雾码头事件与关卡](Docs/DockAdventure.md)
- [低语墓园：30 名敌人、行动、动画与难度试算](Docs/WhisperingGraveyard.md)
- [模块与协作](Docs/Architecture.md)
- [首期玩法规则](Docs/PrototypeRules.md)
- [设计决策记录](Docs/Decisions.md)
