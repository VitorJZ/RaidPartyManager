# 关卡节点图标与码头路标

六类节点图标采用骨白色剪影与旧黄铜描边，背景和图案内的留空为透明。每个图标独立保存为 PNG，源尺寸为 1254 × 1254。Unity 按 Single Sprite 导入，UI 和场景中的 SpriteRenderer 均可引用。

| 节点 | 视觉符号 | 素材 |
| --- | --- | --- |
| 事件 | 问号与菱形点 | [01-Event.png](../Assets/RaidPartyManager/Art/UI/MapNodes/01-Event.png) |
| 城镇 | 三间尖顶建筑 | [02-Town.png](../Assets/RaidPartyManager/Art/UI/MapNodes/02-Town.png) |
| 营地 | 篝火与交叉木柴 | [03-Camp.png](../Assets/RaidPartyManager/Art/UI/MapNodes/03-Camp.png) |
| 商店 | 单个遮棚摊位 | [04-Shop.png](../Assets/RaidPartyManager/Art/UI/MapNodes/04-Shop.png) |
| 普通敌人 | 骷髅 | [05-Enemy.png](../Assets/RaidPartyManager/Art/UI/MapNodes/05-Enemy.png) |
| 精英敌人 | 骷髅与交叉短剑 | [06-EliteEnemy.png](../Assets/RaidPartyManager/Art/UI/MapNodes/06-EliteEnemy.png) |

码头路标使用受潮灰木、麻绳、锈铁支架和暖色旧提灯，参考弥雾码头的背景材质。三块箭头指示牌保留空白，供关卡界面叠加目的地与图标。Sprite 的轴心位于底部中央，便于沿地面摆放。

[迷雾码头路标](../Assets/RaidPartyManager/Art/Props/MistboundDocks-Signpost.png)

![素材总览](Previews/MapAssets.jpg)

<details>
<summary>ImageGen 生成提示词</summary>

素材由内置 ImageGen 分别生成。六个节点使用以下通用说明，每张再添加对应的 Subject。

```text
Use case: stylized-concept. Asset type: a single standalone map-node icon for Raid Party Manager, a dark medieval fantasy roguelike. Generate one isolated icon on a genuinely transparent RGBA background. Square 1:1 canvas. Very concise graphic pictogram, one immediately readable motif, chunky coherent silhouette, very few large shapes, ivory bone fill with only a restrained desaturated antique-brass outer edge and one muted dark separator if essential. Consistent light direction and outline thickness across the icon family. Flat hand-drawn game interface mark with gently worn edges, crisp shape and excellent readability at 32 to 64 pixels. Keep details broad, sparse, and functional. Center the subject and fit it within about 72% of the canvas with consistent clear margins on all sides. The background and the holes inside the symbol must be transparent. No scenery, floor, container, card frame, circle, square badge, panels, labels, title, lettering, numbers, glow, sparkles, drop shadow, photographic texture, tiny linework, realistic 3D rendering or checkerboard pattern.
```

### 事件

```text
Subject: A single bold question mark, drawn as an old ivory carved sign glyph, its dot a tiny solid diamond. The question mark is the only symbol. Clear broad hook, chunky stroke, open space inside the hook.
```

### 城镇

```text
Subject: A compact town silhouette made of three simple adjacent medieval gabled houses with one slightly taller central roof and a small square gate opening. Read unmistakably as a group of buildings. Use just a few roof and wall shapes.
```

### 营地

```text
Subject: A simple campfire pictogram consisting of one chunky flame above two short crossed logs. The fire is an iconic silhouette, with one open interior cutout. The flame and logs are in the same ivory/brass palette.
```

### 商店

```text
Subject: One compact front-facing market stall pictogram, a broad fabric awning with three chunky scallops above one simple counter and two short upright posts. Clearly a single vendor stall, with an open interior silhouette. No merchandise.
```

### 普通敌人

```text
Subject: One plain front-facing skull pictogram with two broad angular eye sockets, a small triangular nose and a short solid squared jaw. Simplify to a readable emblem, broad contour, no jawbone lines, tiny teeth or realistic anatomy. Calm neutral menace.
```

### 精英敌人

```text
Subject: One front-facing skull pictogram in front of two short crossed swords. Skull has two broad angular eye sockets, a tiny triangular nose and solid squared jaw, identical simple skull language to the ordinary encounter marker. The sword blades form a broad clear X visible on either side, so this silhouette is visibly more dangerous than a plain skull. Avoid added microdetails.
```

### 码头路标

参考图为 `Assets/RaidPartyManager/Art/BattleBackgrounds/01-MistboundDocks.png`。

```text
Use case: stylized-concept. Asset type: transparent isolated environmental prop for a side-view dark-fantasy 2D RPG map. The supplied dock picture is a visual style, material and climate reference only. Create ONE freestanding harbor wayfinding signpost that looks naturally at home on that old fogbound quay. Portrait 2:3 composition. A slightly leaning stout weather-beaten timber post with THREE broad blank directional arrow boards at staggered heights, two point right and one left. Each board has a generous simple flat face for future labels or map symbols to be added in game, the boards are separated and do not obscure each other. Muted grey-green salt-stained wood, a few broad worn planks and splintered corners, rusty iron brackets, a thick hemp rope binding, a very small old iron lantern hung from an upper hook with a restrained warm amber light. Damp harbor materials, quiet low-saturation teal shadows, charcoal outlines and sparse ochre highlights. Hand-painted fine pixel-art rendering matching the reference game background, clear sturdy silhouette, side-on front three-quarter view with readable board faces. Small sparse barnacles near the post foot suggest a coastal setting. Entire object and lantern contained within the frame with clear margins. Genuinely transparent RGBA background and transparent gaps between parts. No buildings, water, fog sheet, sky, land scenery, large base, characters, props outside this one signpost, lettering, symbols on boards, numbers, UI, ornate frame, display board around the image, watermark or checkerboard. Preserve the natural alpha around lantern light. Generate a new separate prop, not the background from the reference.
```

</details>
