# 码头商店

在城镇选择「商店」进入 `Assets/Scenes/DockShop.unity`。每次进入测试场景以 999 金币开始，金币显示在顶栏。

四件商品并排摆在柜台上，每次随机选择两件 T1 和两件 T2／T3 商品，同次访问的四件商品互不重复，四个台面位置随机排列。装备档位沿用 Excel「装备图鉴」，有售价的装备与药水按表中基价的 85% 四舍五入。技能书沿用 T1／T2／T3 各 35／25／15 金币的基价。商品界面显示名称、效果和本次售价，档位供内部配货和商人表情使用。

每次进店为各商品生成一次售价，计算为 `ceil(基础价格 × 随机系数)`，系数范围为 0.7～1.3。悬停、查看近景和购买沿用本次售价，实际扣款与标价一致。

柜台后是人才市场的女说书人，穿着同一套高帽、假胡子和单片眼镜的戏服。

| 指针或操作 | 说书人反馈 |
| --- | --- |
| 空白处 | 16 帧呼吸待机 |
| T2 或 T3 商品 | 摊手招呼客人 |
| T1 商品 | 举起金币 |
| 买卖成交 | 笑着轻跳，约 1 秒后恢复待机或当前悬停表情 |

点击柜台查看商品，镜头会推进到台面。悬停查看详情，长按商品约半秒完成购买。长按时松手或移开指针会取消。点击空白处或按 Esc，镜头退回商店全景。购买成功后扣除标价，商品从台面移除并放入当前队伍的共享背包。金币不足时显示提示。

点击柜台近景中的「出售背包物品」，勾选任意多种闲置装备和未学习的技能书，用加减按钮调整同类数量，再按合计件数和金额一次结算。售价按基础价的一半向下取整，Boss 专属装备和技能书每件回收 100 金币。正式讨伐中的收入沿用队伍钱包的欠薪结清规则。

## 角色招募转盘

弥雾码头、低语墓园和机巧遗都的随机事件都可能开出「天降招募转盘」。每趟讨伐共用一次遇见机会，直接离开也会消耗这次机会。

队伍到场后，八格木铜转盘从空中落下。转盘落稳后镜头推进，「转动转盘」下方的两位数滚轮显示当次费用。点击空白处或按 Esc 可以退回全景，点击转盘可再次查看。

转盘从尚未入队的角色中抽出七名一阶角色和一名二阶角色填入扇格。抽中的格子变灰，候选人从右侧走到台前。悬停可查看属性和性格，长按并拖到柜台可洽谈合同。可连续抽取，最多签下一人。队伍满员时可以替换非暂离队员。

首次抽取消耗 15 金币，之后每次加 5 金币。选择「继续赶路」即可离开。进度随讨伐保存，续玩时保留已抽角色、当前价格和签约结果。

转盘中 38 名候选角色的正视呼吸图集按图鉴编号存放于 `Art/Shop/Resources/RecruitmentFront/`。每张图集为四列四行，按从左到右、从上到下的顺序读取 16 帧。正视底图由内置 ImageGen 依据角色立绘及营地坐姿生成，提示为「保留每个角色的脸、装备、配色和笔触，生成透明底、正面站立的完整身形」。16 帧在此基础上以固定脚底为支点，逐帧展开并放松肩胸和衣摆。候选人沿用战斗步态跑到台前，再转为正面站姿。站姿按头顶到脚底的距离等比缩放，脚底对齐台面。转盘扇格从立绘上方居中取景，保留原画宽高比。

商品和价格牌放在柜台上。柜台远景的右侧有一块带城镇图标的石质路牌，点击返回城镇主页，再从主页选择下一站。

![商店预览](Previews/DockShop.png)

柜台、货架和室内背景位于 `Art/Shop/`。柜台后的女说书人和人才市场共用 `StoryEvents/Cast/` 下的素材。站姿按 4 × 4 排列，半身表情按 8 × 8 排列，每种表情占连续两行，共 16 帧。

| 素材 | 用途 |
| --- | --- |
| [DockShop-Interior.png](../Assets/RaidPartyManager/Art/Shop/DockShop-Interior.png) | 码头商店室内背景 |
| [BrokerIdle.png](../Assets/RaidPartyManager/Art/DockPlayhouse/Resources/StoryEvents/Cast/BrokerIdle.png) | 女说书人 16 帧待机 |
| [BrokerPortraits.png](../Assets/RaidPartyManager/Art/DockPlayhouse/Resources/StoryEvents/Cast/BrokerPortraits.png) | 看货和成交时的说书人半身像 |
| [DockShop-Shelf.png](../Assets/RaidPartyManager/Art/Shop/DockShop-Shelf.png) | 四格透明货架 |
| [DockShop-Counter.png](../Assets/RaidPartyManager/Art/Shop/DockShop-Counter.png) | 透明收银台 |

Play Mode 用例 `DockShopTests` 覆盖菜单入口、随机商品构成、浮动售价、三种表情、柜台镜头推进与退回、长按取消、购买扣款、共享背包、余额不足和重复购买保护。表情用例比较实际渲染到屏幕的面部像素，确认悬停后可见画面发生变化并能回到基础微笑。

<details>
<summary>ImageGen 生成提示词</summary>

背景、三表情图集、货架和收银台由内置 ImageGen 分别生成，以 `Art/BattleBackgrounds/01-MistboundDocks.png` 为风格参考。大笑立绘以三表情图集的右侧一帧为参考，通过内置 ImageGen 编辑。

### DockShop-Interior

```text
Use case: stylized-concept. Production art for the 2D dark fantasy game Raid Party Manager. Match the fine hand-painted pixel-art treatment of the provided fogbound medieval dock image, muted sea-green shadows, salt-darkened oak and old brass, restrained amber lantern light, clear readable silhouettes, coherent side-view game composition. No UI, letters, numbers, logos, borders, captions or watermark. Generate one landscape 16:9 interior backplate of a small harbor curio-and-equipment shop, viewed almost straight-on from the customer's side. Wide lens, weathered timber beams, broad dark floor, side shutters with distant fog-blue dock and worn ropes visible, warm hanging lamps high overhead, nets and shelves of a few indistinct jars only along the far upper back wall. Reserve the entire LEFT 45% as quiet dim space for an independently animated merchant behind a counter that will be added later. Reserve the entire RIGHT 42% as a flat dim warm timber wall for a 2 by 2 display cabinet added later. Keep these two staging areas clean and unobstructed. Strong atmosphere in the top beams and far edges, low-contrast midsection. Empty shop room, no characters, counter, freestanding furniture, merchandise display cabinet or obvious focal objects on either reserved side.
```

### DockShop-Merchant

```text
Use case: stylized-concept. Production art for the 2D dark fantasy game Raid Party Manager. Match the fine hand-painted pixel-art treatment of the provided fogbound medieval dock image, muted sea-green shadows, salt-darkened oak and old brass, restrained amber lantern light, clear readable silhouettes, coherent side-view game composition. No UI, letters, numbers, logos, borders, captions or watermark. Create a TRANSPARENT animation character atlas with EXACTLY THREE equally wide vertical cells side by side in one horizontal row, 3 columns by 1 row. Wide landscape atlas ideally 3072 by 1536. Each cell depicts the SAME friendly adult middle-aged human harbor merchant seen from the waist up at EXACTLY the same size, alignment, body silhouette, head position, clothing and stationary hands resting in front. He has a broad expressive face, bushy salt-and-pepper moustache, a slightly crooked leather dock cap, a weathered dark burgundy coat, olive waistcoat and brass buttons. His bare hands are anatomically natural with fingers together at waist level, simple pose, no held items. Three cells change ONLY facial expression. LEFT cell: welcoming small gentle smile, relaxed eyes. MIDDLE cell: much happier broad tooth-showing grin, cheeks raised, bright happy eyes. RIGHT cell: wildly delighted enormous grin with eyes happily squeezed shut and high cheeks, completely joyful merchant anticipating a lucrative deal. Make differences very clear even at small size. Keep his jaw and full head contained, enough identical transparent padding in all cells, no clipping, no overlapping between cells. ALL cell backgrounds and gaps truly transparent RGBA. Same fixed front three-quarter orientation slightly turned toward screen right in all three. No background or counter. Three cells only, no text labels.
```

### DockShop-Shelf

```text
Use case: stylized-concept. Production art for the 2D dark fantasy game Raid Party Manager. Match the fine hand-painted pixel-art treatment of the provided fogbound medieval dock image, muted sea-green shadows, salt-darkened oak and old brass, restrained amber lantern light, clear readable silhouettes, coherent side-view game composition. No UI, letters, numbers, logos, borders, captions or watermark. A single isolated front-facing merchant display cabinet as a transparent RGBA cutout, almost square 1:1. Broad aged salt-stained dark oak framing, subtle rusty iron corner braces, a sturdy horizontal shelf and one vertical divider forming EXACTLY FOUR LARGE EMPTY RECTANGULAR CUBBIES IN A 2 by 2 arrangement. Equal-size cubbies, all four unobstructed, wide flat interiors, simple dark rear wood boards giving contrast for colorful inventory icons. Cabinet front faces squarely toward viewer with little perspective distortion so UI elements fit naturally in each cubby. Wooden framing occupies only outer 7 percent and thin central dividers. Top and bottom rows have equal heights. Whole cabinet has 8 percent transparent outer padding and square proportions. No drawers, extra compartments, objects, merchandise, gems, weapons, signs, price labels or character.
```

### DockShop-Counter

```text
Use case: stylized-concept. Production art for the 2D dark fantasy game Raid Party Manager. Match the fine hand-painted pixel-art treatment of the provided fogbound medieval dock image, muted sea-green shadows, salt-darkened oak and old brass, restrained amber lantern light, clear readable silhouettes, coherent side-view game composition. No UI, letters, numbers, logos, borders, captions or watermark. A single isolated small harbor shop checkout counter as a genuinely transparent RGBA cutout. Wide horizontal 5:2 composition. Almost front facing, dark worn plank front, broad simple pale weathered oak countertop with a clearly visible top surface taking the upper quarter. Low sturdy proportions, a few square wrought iron corner brackets. One small unmarked brass coin tray at the far right edge, leaving most of the countertop empty for a player to drop purchased equipment. Restrained warm top-edge highlights. The whole front face and countertop are contained in frame with clear transparent margins. No merchant, room, background, floor, merchandise, text, symbols, signs or large decorative clutter.
```

### DockShop-Merchant-Laugh

```text
Use case: identity-preserve. Asset type: one square transparent PNG expression frame for Raid Party Manager's shopkeeper. Edit the reference to give this exact merchant a warm, hearty, clearly wide-open laugh, with upturned cheeks, crinkled eyes, distinct upper teeth and a visible dark open mouth. Match the original square framing, face placement, hat, gray curled moustache, stubble, clothes, hands, pose, colors and painterly pixel-art rendering pixel-for-pixel wherever possible. Face and jaw are the only changes. Keep the entire same waist-up silhouette and hands anchored so it can replace the reference in-game without jumping. Single portrait, genuine transparent background, no text or effects.
```

</details>
