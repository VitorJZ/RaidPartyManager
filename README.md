# 讨伐队经理 · Raid Party Manager

四人小队 Roguelike 自动战斗游戏。玩家像球队经理一样挑选阵容、安排站位与装备。开战后，队员根据特性、局势和自己的性格行动。

## 运行首期原型

1. 克隆仓库后运行 `git lfs pull` 下载立绘、图标和技能特效图集，再用 Unity Hub 打开仓库根目录。编辑器版本为 **Unity 6000.6.9f1**。
2. 在 Unity 点击 Play 进入[主菜单](Assets/Scenes/MainMenu.unity)。选择「战斗」「模板」「商店」「营地」或「事件选择」进入对应场景，在顶栏的设置中可返回主菜单，模板场景保留独立的返回按钮。商店测试以 999 金币开始，将货架商品拖到收银台购买。营地显示当前队伍在篝火两侧休息的坐姿，可在「角色编辑」替换成员和站位。界面建议使用 16:9，例如 1280×720。Windows 演练界面优先使用系统微软雅黑显示中文。
3. 首次进入演练时，己方从 35 名角色中抽 4 名，敌方从 9 名敌人中抽 4 名，同侧不会抽到重复角色。用「角色编辑」更换己方成员、站位或装备，在设置中打开「敌方编辑」更换敌人或调整其站位。
4. 点顶栏的「开始战斗」。每回合己方先行动，敌方随后行动，两边各自重新随机排序。当前行动的动画播完后，下一名角色立即行动。角色从屏幕两侧按站位顺序入场。姓名显示在上方，红色生命条与状态图标位于角色下方并随回放更新。悬停状态图标可查看效果。右下方以羊皮纸显示战斗日志。每回合结束后，点顶部「下回合」继续，或长按拖动一名存活队员到另一名存活队员身上。两人换位后自动开始下一回合。胜利弹窗中点击狂战士之印和金币图标领取，全部领完返回主菜单。「重置演练」时，未编辑的一方重抽阵容，编辑过的一方保留选定角色。同一次 Play 中，切换场景和重置演练都会保留配装和共享背包。停止后重新点击 Play 会恢复初始装备。

战前和败北结算画面的「角色编辑」可打开队伍总览。长按立绘约 0.3 秒可拖拽换位，也能拖到空位或移出站位。点按立绘打开角色面板和 4×4 分页背包。测试背包初始有 50 种装备各 4 件，技能书各一本。悬停查看效果，长按物品可拖到装备槽。

职业边框会随角色类型应用到战斗站位、装备缩略图和角色编辑页。查看[新版角色立绘总览](Docs/Previews/ExpressivePortraits.jpg)和[图鉴更新说明](Docs/WorkbookUpdate.md)。

「事件选择」可以直接检查九种事件与六类场景；切换「关卡试游」可运行含三处分叉、第十关 Boss 的完整路线。检查队伍独立保存，背包、金币、宝箱伙伴和投资会在场景之间同步。参阅[事件与关卡说明](Docs/DockAdventure.md)。

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

## 开发入口

- [战斗界面与状态栏](Docs/BattleUI.md)
- [20 种技能特效的动画预览与制作说明](Docs/SkillEffects.md)
- [码头商店](Docs/DockShop.md)
- [营地场景与 35 名角色坐姿](Docs/Camp.md)
- [弥雾码头事件与关卡](Docs/DockAdventure.md)
- [模块与协作](Docs/Architecture.md)
- [首期玩法规则](Docs/PrototypeRules.md)
- [设计决策记录](Docs/Decisions.md)
