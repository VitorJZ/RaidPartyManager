# 码头商店

从主菜单选择「商店」进入 `Assets/Scenes/DockShop.unity`。每次进入测试场景以 999 金币开始，金币显示在顶栏。

右侧货架为 2 × 2，每次随机选择两件 T1 和两件 T2／T3 商品，同次访问的四件商品互不重复，四个货架位置随机排列。内部的 Shop Tier 和基础 Shop Price 来自 Excel「装备图鉴」当前的 T 位与价格列。商品界面显示名称、效果和本次售价，档位供内部配货和商人表情使用。

每次进店为各商品生成一次售价，计算为 `ceil(基础价格 × 随机系数)`，系数范围为 0.7～1.3。悬停、拖拽和购买沿用本次售价，实际扣款与标价一致。

| 指针或操作 | 商人反馈 |
| --- | --- |
| 空白处 | 基础微笑 |
| T2 或 T3 商品 | 更开心的笑容 |
| T1 商品 | 最开心的笑容 |
| 购买成功 | 切换到张嘴大笑立绘，约 1 秒内上下抖动，再恢复当前悬停对应的表情 |

按下商品并拖到左侧收银台区域，松开鼠标即可购买。拖到其他位置松开会返回货架，按 Esc 可取消。购买成功后扣除标价、将该格标为已售出，并把物品加入当前 Play 会话的共享背包。金币不足时保留商品与金币，并显示提示。

![商店预览](Previews/DockShop.png)

背景、商人、货架和收银台分别存于 `Assets/RaidPartyManager/Art/Shop/`。商人图集为三列一行，从左往右依次是基础微笑、开心和最开心的笑容。三个表情共用人物尺度与身体姿势。成交时显示单独的透明立绘 `DockShop-Merchant-Laugh.png`。

| 素材 | 用途 |
| --- | --- |
| [DockShop-Interior.png](../Assets/RaidPartyManager/Art/Shop/DockShop-Interior.png) | 码头商店室内背景 |
| [DockShop-Merchant.png](../Assets/RaidPartyManager/Art/Shop/DockShop-Merchant.png) | 三表情透明图集 |
| [DockShop-Merchant-Laugh.png](../Assets/RaidPartyManager/Art/Shop/DockShop-Merchant-Laugh.png) | 成交时的张嘴大笑立绘 |
| [DockShop-Shelf.png](../Assets/RaidPartyManager/Art/Shop/DockShop-Shelf.png) | 四格透明货架 |
| [DockShop-Counter.png](../Assets/RaidPartyManager/Art/Shop/DockShop-Counter.png) | 透明收银台 |

Play Mode 用例 `DockShopTests` 覆盖菜单入口、随机商品构成、浮动售价、三种表情、拖拽取消、购买扣款、抖动归位、共享背包、余额不足和重复购买保护。表情用例比较实际渲染到屏幕的面部像素，确认悬停后可见画面发生变化并能回到基础微笑。

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
