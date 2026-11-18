# 营地场景与坐姿立绘

主菜单选择「营地」进入 `Assets/Scenes/Camp.unity`。场景显示当前出战队伍，与战斗、商店共用同一次 Play 中的角色和装备。右上角「角色编辑」可右键替换队员、长按拖拽换位，返回营地后显示对应的坐姿。

## 场景构图

使用轻微俯视角，夜间海岸林地与远处码头雾景。两根木头呈倒 V 形，篝火在中间偏下。座位与用户参考图一致。

| 位置 | 队伍序号 | 原始朝向 | 镜像 |
| --- | --- | --- | --- |
| 左后 | 3 | 向右下看篝火 | 否 |
| 左前 | 4 | 向右下看篝火 | 否 |
| 右后 | 2 | 向右下看篝火 | 水平镜像 |
| 右前 | 1 | 向右下看篝火 | 水平镜像 |

先绘制背景和后排角色，再绘制前排角色与独立篝火。根据画面比例整体缩放，保持角色、木头和篝火之间的位置关系。篝火有轻微的亮度和形变变化。

## 美术资产

- `Assets/RaidPartyManager/Art/Camp/Camp-Background.png`：完整营地背景、两根木头和石围火塘。
- `Assets/RaidPartyManager/Art/Camp/Camp-Fire.png`：透明背景的独立篝火。
- `Assets/RaidPartyManager/Art/Camp/Seated/`：35 名角色各一张 1:1 透明坐姿 PNG。角色保留原立绘的脸、发型、服饰、盔甲和代表物，膝盖自然弯曲，双手放在腿上或膝前，面部保持可见。
- 每个 `CharacterAsset` 的 `campSeatedSprite` 对应其坐姿，`EncounterData.CampSpriteFor` 供场景读取。
- [完整坐姿总览](Previews/CampRoster.jpg)、[营地实机预览](Previews/Camp.png)、[原始布局参考](References/Camp-Layout.png)。

所有素材由 `image_gen.imagegen` 制作，坐姿参考现有角色立绘，背景参考布局图和迷雾码头风格。原始生成 PNG 保留，Unity 使用拷贝。每张图的完整提示词、参考图及透明背景设置记录在 [CampArtPrompts.json](CampArtPrompts.json)。

## 验证

`CharacterSyncTests` 检查图鉴 1～35 号角色全部具有方形坐姿素材。`CampTests` 从主菜单进入营地，检查四个座位和两侧镜像方向，替换一名角色后返回营地及战斗，验证两处使用同一支队伍。
