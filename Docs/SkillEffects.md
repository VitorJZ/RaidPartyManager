# 技能特效

[动画总览](Previews/SkillEffects.gif)按技能书编号排列。技能名称、效果、目标和费用以《讨伐队经理-图鉴.xlsx》的「技能图鉴」工作表为准，当前动画总览包含 37 项技能。参阅[图鉴更新记录](WorkbookUpdate.md)。

| 编号 | 技能 | 画面与播放时机 |
| --- | --- | --- |
| 01 | 战争咆哮 | 施法者先聚起余烬，铜红色气浪向两侧爆开，随后散成火星。 |
| 02 | 熊熊烈火 | 所有敌方受击位置各升起一道橙红火墙。 |
| 03 | 极度冰点 | 实际获得冰冻的最多两名目标身边升起蓝白冰棱。 |
| 04 | 阳光普照 | 每个结算位置都从高处洒下金色光束。 |
| 05 | 坚如磐石 | 施法者身前升起带亮色裂隙的花岗岩甲，提示保护已就绪。 |
| 06 | 妙手回春 | 实际结算的位置出现上升的翠绿治疗光带。 |
| 07 | 万箭齐发 | 蓄势后只拉弓一次，同时向各敌方目标发射专用箭矢。箭矢抵达后消失，每个命中位置播放青色冲击特效。 |
| 08 | 星陨术 | 蓄势后挥动法杖一次，从敌方队伍正上方垂直落下一颗陨石。陨石撞上阵列中部的目标后裂开，所有命中的敌人在这个时刻扣血。 |
| 09 | 横扫千军 | 蓄势后冲锋挥砍一次，在各命中位置同时出现掠地的铜红刀光。 |
| 10 | 闪电鞭 | 同一目标处出现多次闪烁的蓝紫蛇形电弧。同一施法的多段伤害共用一段动画。 |
| 11 | 毒瘴爆发 | 中毒目标处扩散黄绿色毒雾与亮粒。 |
| 12 | 血肉盛宴 | 被吞食的尸体和增长生命的施法者处各出现一次暗红旋涡。 |
| 13 | 闪身步 | 施法者身边掠过青绿残影和金色亮点，表现敏捷与盛气。 |
| 14 | 金钟罩 | 当回合获得保护的每名友方各出现一座半透明钟罩。 |
| 15 | 火眼金睛 | 破绽生效的目标处出现浮现在火雾里的双瞳。 |
| 16 | 猩红收割 | 受击目标与获得回复的位置出现猩红镰弧。 |
| 17 | 英雄赞歌 | 获得纯净的友方处升起层叠的淡紫与金色光带。 |
| 18 | 震慑八方 | 获得眩晕的敌方处出现压向目标的暗紫气浪与落灰。 |
| 19 | 裂地震击 | 实际受击的主目标及相邻单位处出现地裂和飞石。 |
| 20 | 穿云箭 | 命中位置飞过亮蓝长箭并留下穿透环。 |

## 资源与播放

- 图集在 `Assets/RaidPartyManager/Art/SkillEffects`。主特效为 16 帧横排透明 PNG。重绘的 33 套为 2048 × 128，使用线性过滤和无 Mipmap 设置。四套早期画稿为 1536 × 96。替换同一技能的图集时保留其 `.meta`。
- 在 `SkillCatalog` 的技能条目上设置 `Battle Effect`。万箭齐发还绑定了 `Projectile`，同一释放动作会为每个实际目标创建一支飞行箭矢。星陨术的 `Projectile` 和 `Projectile Fragments` 分别绑定完整陨石和四块碎片。第 08 号图集只呈现命中后的冲击波。战斗按技能书 ID（例如 `skill-book-07`）查纹理。若元素石英改变了直接技能的命中元素，回放用相应的火焰或雷电图集，日志保留原技能名。熊熊烈火转成雷电后走雷电结算：忽略特防、应用雷电加成，也不会触发火伤专有的寒冷加成与清除。万箭齐发、星陨术、横扫千军共用玩法效果，但分别保留技能书 ID，因而播放不同动画。
- 范围攻击只有首个命中条目驱动角色动作，后续命中在同一个冲击时刻应用。荆棘、反击和额外攻击在这一批命中结算后处理。治疗、伤害和状态用结算事件中的目标定位。蓄势提示、金钟罩和吞食尸体使用纯视觉事件。未生效的状态和未命中的后续横扫不产生目标动画。护盾等免伤仍可显示命中反馈。
- 回放在结算点按目标播放图集。标准时长 0.72 秒，随战斗播放速度变化。结束、退场或重置时清理效果。特效与动画计时由界面处理，技能费用和结果仍由战斗模拟器结算。

普通箭矢、魔法弹、敌方投射物、穿透箭及其尾粒、万箭齐发的箭矢和星陨术的陨石，各使用一张 4 × 4 的飞行动画图集。陨石的四块碎片各有 16 帧，依次排在同一张 8 × 8 图集上。运动轨迹与扣血时刻按原飞行时间计算。

## 修改与验证

33 套新画稿由内置 ImageGen 生成，原图和提示存放在[技能画稿](References/SkillEffects)中。用 `python3 Tools/BuildPaintedSkillEffects.py` 构建帧图，再执行 `python3 Tools/GenerateSkillEffects.py --preview` 校验图集并更新预览。

Unity Edit Mode 验证范围技能的目标及三种蓄势技能的延后触发。Play Mode 在战斗场景装备「战争咆哮」技能书，验证图集换帧并在回合结束前清理。万箭齐发使用三敌人验证同时出现三支飞行箭矢、三个命中特效和三次对应扣血。星陨术验证单颗陨石竖直下落、接触时整队扣血、四块碎片扩散并清理。


## 万箭齐发的飞行素材

专用箭矢使用内置 ImageGen 生成。透明 PNG 存放在 [07-VolleyProjectile.png](../Assets/RaidPartyManager/Art/SkillEffects/07-VolleyProjectile.png)。命中时使用重绘后的青白箭迹和羽屑画稿。

> Use case: stylized-concept. Asset type: a flying arrow combat VFX sprite for an original dark fantasy pixel-art Unity auto battler. Exactly one thin horizontal fantasy arrow pointing RIGHT. Sharp pale ivory steel arrowhead at right, luminous turquoise shaft, two short teal feather vanes at the left, a tapered narrow turquoise wind streak extending behind the fletching to the left. Bold dark contour and a tiny ivory highlight, hand-drawn crisp pixel clusters, readable as one projectile at 48 pixels wide. Strict side orthographic view, no depth or diagonal. Center the shaft on the horizontal midline in a wide canvas (3:1 if supported), from 5% through 95% of the canvas width. Transparent background with genuine alpha, only the projectile and attached streak. No bow, hands, target, figures, lettering, ground shadow, UI, detached particles, or outlines around the canvas.


## 星陨术的陨石素材

完整陨石和四块碎片使用内置 ImageGen 生成。[陨石 PNG](../Assets/RaidPartyManager/Art/SkillEffects/08-MeteorBoulder.png) · [碎片图集 PNG](../Assets/RaidPartyManager/Art/SkillEffects/08-MeteorFragments.png)

> Use case: stylized-concept. Asset type: one midair meteor body for a dark fantasy pixel-art Unity auto battler. Transparent square PNG. Show exactly one heavy irregular angular carbon-black and violet meteor rock with a few narrow brilliant lavender-magenta glowing lava fissures and hot purple rim light. The whole silhouetted slab must read as one solid heavy boulder, about 78 percent of canvas. Compact diagonal oval, most mass at center, broad crisp angular facets with chunky hand-painted pixel clusters, charcoal outline and strong contrast. Front game-sprite view. Nearly straight on, bottom-right edge forward. Keep its entire shape cleanly readable at 72 pixels. A few hints of incandescent edges, no fire trail, smoke, halo, sparks, disconnected particles, shadow outside object, scenery, lettering, border, or UI. Truly transparent negative space outside silhouette.

> Use case: precise-object-edit. Asset type: square transparent 2-by-2 sprite atlas for four physical fragments of the referenced pixel art meteor. Break the exact black-violet rock in Image 1 into FOUR detached irregular stone chunks, with bright magenta and pale lavender fracture faces on the cut edges. Put exactly one chunky fragment fully inside each quadrant of the square: NW, NE, SW, SE; one fragment centered at each of these four quadrant centers. The four pieces should look like unequal jagged quarters of the same meteor with complementary new broken faces and its old dark exterior, but leave very clear wide transparent gutters across the entire horizontal and vertical canvas midlines, plus a clear outer margin. Each chunk occupies roughly 70 percent of its own quadrant. The art matches the referenced hand-painted pixel clusters, dark silhouette, front angle and palette. True transparent alpha. No lines dividing the atlas, no slots, text, glowing aura around pieces, particles, debris outside the four pieces, or background.

## 本轮重绘的特效

熊熊烈火从地面窜起，火舌分开后散作余烬。攻城矢斜向砸入，碎石和铜屑从落点弹出。雷暴裁决以独立的 16 帧聚云演出提示蓄力，下一回合在敌人身上播放另一套 16 帧落雷。深水翻涌的浪峰短暂呈现巨魟的轮廓，碎浪落下后收成水纹。

四套透明原画由内置 ImageGen 生成。各图使用以下共同提示，另附各技能的动作描述。

> Use case: stylized-concept. Asset type: transparent animated combat VFX sprite atlas for a side-on medieval dark-fantasy pixel-art theatre RPG. Produce ONE square image 4 columns by 4 rows, exactly SIXTEEN successive cels, read left to right and top to bottom. Equal square cells of identical size, effects perfectly centered at the same ground origin in each cell with ample empty margins. No grid marks, border, text, numbers, backdrop, ground, environment, characters, panels or UI icon. First row windup, second impact, third expansion, fourth dissipates, even frame spacing. Designed to read at 96 pixels per frame with weighty pixel clusters, strong silhouette, hand-painted shaded masses, limited palette, selective highlights and no blurry photographic glow. True transparent alpha outside each isolated cel.

> Blazing Fire. Molten ember orange and pale gold. A small spark ignites on the ground, a crown of three curling fire tongues surges up and outward to engulf a target, separates into tapered tongues, dies down into embers. Organic billowing shapes and charcoal red bases.

> Siege bolt impact. A heavy short bronze ballista bolt tears diagonally down from upper left to lower right into an invisible target, a fan of brass splinters and small charcoal stone chips erupts, glowing dust and crescent shock wave taper out. Single decisive impact, clear motion and asymmetric silhouette.

> Thunder Verdict. A fork of indigo cloud charge gathers over the target, collapses into a jagged brilliant ivory lightning strike connecting sky and ground, forks sideways into smaller violet arcs, then discharges into fading ground sparks. Irregular lightning branches, a strong bright contact point.

> Deepwater Surge of a ghostly giant manta ray. Midnight teal and glacier blue currents curl into a rising watery crest, surge over the target with a fleeting manta-wing silhouette in the foam, collapse into outward white spray and falling droplets. Hand-painted rolling foam, clear silhouette.
