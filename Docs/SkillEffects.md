# 技能特效

[动画总览](Previews/SkillEffects.gif)按技能书编号排列。技能名称、效果、目标和费用以《讨伐队经理-图鉴.xlsx》的「技能图鉴」工作表为准，2026-11-18 与 Unity 的 20 项技能逐条核对。参阅[图鉴更新记录](WorkbookUpdate.md)。

| 编号 | 技能 | 画面与播放时机 |
| --- | --- | --- |
| 01 | 战争咆哮 | 施法者身上扬起橙色角形气浪，提示下一次攻击蓄势。 |
| 02 | 熊熊烈火 | 所有敌方受击位置各升起一道橙红火墙。 |
| 03 | 极度冰点 | 实际获得冰冻的最多两名目标身边升起蓝白冰棱。 |
| 04 | 阳光普照 | 每个实际结算的位置出现向下洒落的金色日轮。 |
| 05 | 坚如磐石 | 施法者身前升起橙褐岩甲，提示保护已就绪。 |
| 06 | 妙手回春 | 实际结算的位置出现浅绿光环和上升的治疗十字。 |
| 07 | 万箭齐发 | 蓄势后只拉弓一次，同时向各敌方目标发射专用箭矢。箭矢抵达后消失，每个命中位置播放青色冲击特效。 |
| 08 | 星陨术 | 蓄势后挥动法杖一次，从敌方队伍正上方垂直落下一颗陨石。陨石撞上阵列中部的目标后裂开，所有命中的敌人在这个时刻扣血。 |
| 09 | 横扫千军 | 蓄势后冲锋挥砍一次，在各命中位置同时出现宽幅金色刀光。 |
| 10 | 闪电鞭 | 同一目标处出现多次闪烁的蓝紫蛇形电弧。同一施法的多段伤害共用一段动画。 |
| 11 | 毒瘴爆发 | 中毒目标处扩散黄绿色毒雾与亮粒。 |
| 12 | 血肉盛宴 | 被吞食的尸体和增长生命的施法者处各出现一次暗红旋涡。 |
| 13 | 闪身步 | 施法者身边掠过青绿残影和金色亮点，表现敏捷与盛气。 |
| 14 | 金钟罩 | 当回合获得保护的每名友方各出现一座半透明钟罩。 |
| 15 | 火眼金睛 | 破绽生效的目标处出现火瞳和菱形标记。 |
| 16 | 猩红收割 | 受击目标与获得回复的位置出现猩红镰弧。 |
| 17 | 英雄赞歌 | 获得纯净的友方处升起带金色亮点的淡紫音符。 |
| 18 | 震慑八方 | 获得眩晕的敌方处出现扩散的紫色冲击环和旋转星标。 |
| 19 | 裂地震击 | 实际受击的主目标及相邻单位处出现地裂和飞石。 |
| 20 | 穿云箭 | 命中位置飞过亮蓝长箭并留下穿透环。 |

## 资源与播放

- 图集在 `Assets/RaidPartyManager/Art/SkillEffects`。每套是 1536 × 96 的透明 PNG，从左到右 16 帧，每帧 96 × 96。纹理使用点过滤、原始尺寸、RGBA 和无 Mipmap 设置。替换同一技能的图集时保留其 `.meta`。
- 在 `SkillCatalog` 的技能条目上设置 `Battle Effect`。万箭齐发还绑定了 `Projectile`，同一释放动作会为每个实际目标创建一支飞行箭矢。星陨术的 `Projectile` 和 `Projectile Fragments` 分别绑定完整陨石和四块碎片。第 08 号图集只呈现命中后的冲击波。战斗按技能书 ID（例如 `skill-book-07`）查纹理。若元素石英改变了直接技能的命中元素，回放用相应的火焰或雷电图集，日志保留原技能名。熊熊烈火转成雷电后走雷电结算：忽略特防、应用雷电加成，也不会触发火伤专有的寒冷加成与清除。万箭齐发、星陨术、横扫千军共用玩法效果，但分别保留技能书 ID，因而播放不同动画。
- 范围攻击只有首个命中条目驱动角色动作，后续命中在同一个冲击时刻应用。荆棘、反击和额外攻击在这一批命中结算后处理。治疗、伤害和状态用结算事件中的目标定位。蓄势提示、金钟罩和吞食尸体使用纯视觉事件。未生效的状态和未命中的后续横扫不产生目标动画。护盾等免伤仍可显示命中反馈。
- 回放在结算点按目标播放图集。标准时长 0.72 秒，随战斗动画速度和暂停状态变化。结束、退场或重置时清理效果。特效与动画计时由界面处理，技能费用和结果仍由战斗模拟器结算。

## 修改与验证

修改 [生成脚本](../Tools/GenerateSkillEffects.py) 中对应编号的绘制和配色。使用安装了 Pillow 的 Python 3.9+ 从项目根目录运行 `python Tools/GenerateSkillEffects.py --preview`，生成图集与预览。

Unity Edit Mode 检查 20 套图集的透明通道和尺寸，并验证范围技能的目标及三种蓄势技能的延后触发。Play Mode 在战斗场景装备「战争咆哮」技能书，验证图集换帧并在回合结束前清理。万箭齐发使用三敌人验证同时出现三支飞行箭矢、三个命中特效和三次对应扣血。星陨术验证单颗陨石竖直下落、接触时整队扣血、四块碎片扩散并清理。


## 万箭齐发的飞行素材

专用箭矢使用内置 ImageGen 生成。透明 PNG 存放在 [07-VolleyProjectile.png](../Assets/RaidPartyManager/Art/SkillEffects/07-VolleyProjectile.png)。技能命中图集由生成脚本重建。

> Use case: stylized-concept. Asset type: a flying arrow combat VFX sprite for an original dark fantasy pixel-art Unity auto battler. Exactly one thin horizontal fantasy arrow pointing RIGHT. Sharp pale ivory steel arrowhead at right, luminous turquoise shaft, two short teal feather vanes at the left, a tapered narrow turquoise wind streak extending behind the fletching to the left. Bold dark contour and a tiny ivory highlight, hand-drawn crisp pixel clusters, readable as one projectile at 48 pixels wide. Strict side orthographic view, no depth or diagonal. Center the shaft on the horizontal midline in a wide canvas (3:1 if supported), from 5% through 95% of the canvas width. Transparent background with genuine alpha, only the projectile and attached streak. No bow, hands, target, figures, lettering, ground shadow, UI, detached particles, or outlines around the canvas.


## 星陨术的陨石素材

完整陨石和四块碎片使用内置 ImageGen 生成。[陨石 PNG](../Assets/RaidPartyManager/Art/SkillEffects/08-MeteorBoulder.png) · [碎片图集 PNG](../Assets/RaidPartyManager/Art/SkillEffects/08-MeteorFragments.png)

> Use case: stylized-concept. Asset type: one midair meteor body for a dark fantasy pixel-art Unity auto battler. Transparent square PNG. Show exactly one heavy irregular angular carbon-black and violet meteor rock with a few narrow brilliant lavender-magenta glowing lava fissures and hot purple rim light. The whole silhouetted slab must read as one solid heavy boulder, about 78 percent of canvas. Compact diagonal oval, most mass at center, broad crisp angular facets with chunky hand-painted pixel clusters, charcoal outline and strong contrast. Front game-sprite view. Nearly straight on, bottom-right edge forward. Keep its entire shape cleanly readable at 72 pixels. A few hints of incandescent edges, no fire trail, smoke, halo, sparks, disconnected particles, shadow outside object, scenery, lettering, border, or UI. Truly transparent negative space outside silhouette.

> Use case: precise-object-edit. Asset type: square transparent 2-by-2 sprite atlas for four physical fragments of the referenced pixel art meteor. Break the exact black-violet rock in Image 1 into FOUR detached irregular stone chunks, with bright magenta and pale lavender fracture faces on the cut edges. Put exactly one chunky fragment fully inside each quadrant of the square: NW, NE, SW, SE; one fragment centered at each of these four quadrant centers. The four pieces should look like unequal jagged quarters of the same meteor with complementary new broken faces and its old dark exterior, but leave very clear wide transparent gutters across the entire horizontal and vertical canvas midlines, plus a clear outer margin. Each chunk occupies roughly 70 percent of its own quadrant. The art matches the referenced hand-painted pixel clusters, dark silhouette, front angle and palette. True transparent alpha. No lines dividing the atlas, no slots, text, glowing aura around pieces, particles, debris outside the four pieces, or background.
