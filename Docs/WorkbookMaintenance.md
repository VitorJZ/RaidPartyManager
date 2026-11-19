# 图鉴维护

## 已交付文件

- [完整图鉴](Bestiary-WithGraveyard.xlsx)：依据用户提供的《讨伐队经理-图鉴 副本.xlsx》合并。保留角色、敌人、装备、技能、区域、状态、性格、音效清单八个工作表及顺序。
- [墓园增补表](WhisperingGraveyard.xlsx)：30 名墓园敌人、62 项行动、规则与 2200 场固定阵容难度试算。

完整图鉴的「敌人图鉴」第 25–54 行为墓园敌人，序号 24–53。总计 40 普通、9 精英、4 Boss。原 A1:O24 保留，P:T 新增第三行为、第三行为概率、所属区域、占格、立绘。墓园的 30 张 PNG 直接嵌入文件，读取不依赖外部图片路径。动画保存在 Unity 资源与 GIF 预览中。

这次合并更新本地副本，并复制到同步 Obsidian 的 `讨伐队经理/敌人图鉴/低语墓园/`；未回写原在线共享链接。原始附件留在用户本地，仓库保存合并成品和原件哈希。不要把合并成品再次作为仅含 23 名敌人的输入：脚本会拒绝，以免重复追加。

## 数据归属

墓园运行时配置源为 [WhisperingGraveyard.json](WhisperingGraveyard.json)。修改属性或行动后运行 `python Tools/BuildWhisperingGraveyard.py --data-only`，更新 Unity 资源并验证受影响的战斗场景。完整装配命令会重新切分美术原图，数值修改不需要重新生成图片。

Excel 是配置快照，编辑 Excel 不会自动回写 JSON 或 Unity。行动的攻击修正、伤害类型、目标和概率必须与 JSON 一致；Boss 的第三行动在 P/Q 列。中文形象说明来自 `DocumentWhisperingGraveyard.py` 的 `APPEARANCE`。

## 重建合并文件

使用 Codex 提供的捆绑 Node.js、`@oai/artifact-tool` 模块目录和带 lxml 的 Python。三个运行时路径通过 `load_workspace_dependencies` 获取，分别赋给 `$sheetNode`、`$sheetModules`、`$sheetPython`。在仓库根目录执行以下准备步骤，`$originalBestiaryPath` 指向用户提供的原 23 名敌人图鉴。

```powershell
$mergeDir = Join-Path (Get-Location) 'outputs/graveyard-merge-20261119'
New-Item -ItemType Directory -Path $mergeDir -Force | Out-Null
Copy-Item -LiteralPath $originalBestiaryPath -Destination (Join-Path $mergeDir 'source.xlsx')
# 首次准备时建立依赖目录联接；已存在时直接复用。
if (-not (Test-Path (Join-Path $mergeDir 'node_modules'))) {
    New-Item -ItemType Junction -Path (Join-Path $mergeDir 'node_modules') -Target $sheetModules | Out-Null
}
Copy-Item Tools/MergeGraveyardWorkbook.mjs (Join-Path $mergeDir 'MergeGraveyardWorkbook.mjs')
& $sheetPython Tools/DocumentWhisperingGraveyard.py --export-appearance (Join-Path $mergeDir 'appearance.json')
& $sheetNode (Join-Path $mergeDir 'MergeGraveyardWorkbook.mjs') --inspect
```

查看 `source-enemies.png` 确认原表结构后继续：

```powershell
& $sheetNode (Join-Path $mergeDir 'MergeGraveyardWorkbook.mjs') --merge
& $sheetPython Tools/PreserveGraveyardWorkbook.py
& $sheetNode (Join-Path $mergeDir 'MergeGraveyardWorkbook.mjs') --verify
```

artifact-tool 写入新增单元格、样式和图片。保留脚本只取这些新增内容，恢复原文件未修改的部件；其他七个工作表、原生音效表、原有共享字符串、视图与工作簿设置按原件保留。核对原敌人单元格内容与样式、30 名新敌人的数值和概率，检查立绘与文字显示，再将输出复制为 `Docs/Bestiary-WithGraveyard.xlsx`。仅修改数据时仍须重跑受影响的导出核对。

`outputs/` 和 Python 缓存不提交。两份成品 Excel、PNG、GIF 和原始动作图通过 Git LFS 管理。新拉取项目先执行 `git lfs pull`。

## 验收记录

[WhisperingGraveyardValidation.json](Validation/WhisperingGraveyardValidation.json)记录 Unity 测试结果、运行时文件哈希、资源引用与数量、合并图鉴校验、原始附件及成品哈希。测试报告在本地 `Logs/Graveyard/`。本次完整测试通过后，运行时代码和 Unity 内容未再修改；后续 Excel 合并另行核对，未改变战斗结果。
