# 讨伐队经理 · Raid Party Manager

四人小队 Roguelike 自动战斗游戏。玩家像球队经理一样挑选阵容、安排站位与装备。开战后，队员根据技能、局势和自己的性格行动。

## 运行首期原型

1. 克隆仓库后运行 `git lfs pull` 下载立绘和 Buff 图标，再用 Unity Hub 打开仓库根目录。编辑器版本为 **Unity 6000.6.9f1**。
2. 在 Unity 点击 Play 进入[主菜单](Assets/Scenes/MainMenu.unity)。选择「战斗」或「模板」进入对应场景，两个场景都有「返回主菜单」按钮。战斗界面建议使用 16:9，例如 1280×720。Windows 演练界面优先使用系统微软雅黑显示中文。
3. 点击「角色编辑」安排站位。点击装备槽可打开共享背包，把装备分配或转交给队员。
4. 点「开始自动战斗」。每回合己方先行动，敌方随后行动，两边各自重新随机排序。两名角色行动之间间隔 3 秒。角色下方的生命条与状态图标随回放更新，右下方显示战斗日志。每回合结束后，点顶部「下回合」继续，或长按拖动一名存活队员到另一名存活队员身上。两人换位后自动开始下一回合。胜利后狂战士之印放入共享背包。「重置演练」恢复队员的生命与演练站位。同一次 Play 中，切换场景和重置演练都会保留配装和共享背包。停止后重新点击 Play 会恢复初始装备。

战前和结算画面的「角色编辑」可打开队伍总览。长按立绘约 0.3 秒可拖拽换位，也能拖到空位或移出站位。点按立绘打开角色面板和 4×4 分页背包。测试背包初始有 44 种物品各 4 件。悬停查看效果，长按物品可拖到装备槽。

职业边框会随角色类型应用到战斗站位、装备缩略图和角色编辑页。

建议先按默认阵容打一场，再把篝火守夜人换到 4 号位，查看「突袭队尾」首次命中的对象。

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
- [模块与协作](Docs/Architecture.md)
- [首期玩法规则](Docs/PrototypeRules.md)
- [设计决策记录](Docs/Decisions.md)
