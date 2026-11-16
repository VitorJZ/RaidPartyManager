# 讨伐队经理 · Raid Party Manager

四人小队 Roguelike 自动战斗游戏。玩家像球队经理一样挑选阵容、安排站位与装备。开战后，队员根据技能、局势和自己的性格行动。

## 运行首期原型

1. 克隆仓库后运行 `git lfs pull` 下载立绘，再用 Unity Hub 打开仓库根目录。编辑器版本为 **Unity 6000.6.9f1**。
2. 打开 [BattlePrototype](Assets/Scenes/BattlePrototype.unity) 场景并进入 Play Mode。在 Game 视图建议使用 16:9，例如 1280×720。Windows 演练界面优先使用系统微软雅黑显示中文。
3. 点击左下方的队员立绘，再在战场选择站位。点击装备槽可打开共享背包，把装备分配或转交给队员。
4. 点「开始自动战斗」。上方生命条和角色下方的状态栏会随回放更新，右下方显示战斗日志。可以改回放速度或跳到结算。胜利后狂战士之印放入共享背包。「重置演练」恢复初始站位、生命和背包。

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

从仓库根目录执行，按需修改编辑器路径。退出正在使用该项目的编辑器，或者对独立副本运行命令。Play Mode 测试会加载实际的演练场景并通过 UI Toolkit 发送按钮操作。

## 开发入口

- [战斗界面与状态栏](Docs/BattleUI.md)
- [模块与协作](Docs/Architecture.md)
- [首期玩法规则](Docs/PrototypeRules.md)
- [设计决策记录](Docs/Decisions.md)
