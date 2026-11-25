# 普通攻击特效生成提示

使用 Codex 内置 ImageGen。每个画格对应一件战斗特效，画格顺序和角色对应关系见 `Tools/BuildAttackEffects.py`。

共同要求为等距网格、画格四周留足透明间距、无文字和背景。效果使用原创暗色奇幻舞台游戏的手绘风格，强调笔触、内发光、材质层次和有机残迹。具备方向的攻击朝右。

两张通用图组为 4×4，分别以金属斩击、火焰、岩屑、荆棘、箭迹、星芒、月牙、圣光、震荡环、蒸汽、气泡、金币、符印、锁链、飞羽、骨片为题，以及冰晶、雷电、齿轮、碎瓷、浪花、弹丸、缝线、抓钩、墨汁、光束、烛蜡、爪痕、声纹、纸页、针刺、烟尘为题。

下列六张画稿使用 4×4 网格。每条提示按画格顺序列出。

## Company-veterans

1. KindleGuard — blazing molten horseshoe shield cleave with orange coals
2. TwilightKnight — rose-purple twilight sword afterimage with dark crescent moon
3. HeavyBulwark — angular pewter shield-bracing bash with bright chipped iron
4. ArcaneApprentice — lilac rune fragments around a slanted magic staff streak
5. VoidWeaver — ragged black-violet swallowtail vortex and lavender sparks
6. StarfireMage — hot gold burning meteor shard with blue rim and sweeping flame
7. EmberBrawler — scarlet blunt gauntlet punch bloom and sparks
8. ValiantSoldier — scarlet-and-copper two diagonal sword slashes
9. Apocalypse — crimson occult seal bursting apart with magenta ribbons
10. ForestStalker — teal triple claw wind with torn foliage
11. StarChaser — three cold blue-starlight arrow-like comets
12. EclipseArcher — indigo crescent arrow with pale blue lunar trail
13. TianGong — bright elongated golden celestial arrow trails
14. DivinePriest — ivory amber radiant spear cross with warm white sparks
15. FallenAcolyte — rose-purple scythe-shaped corrupted wound glow with dark flame
16. GildedMerchant — fan of glittering brass ingots and amber stamped coin flashes, no letters

## Docks-company

1. DeepSeaWhisper — horizontal luminous blue-green razor-shaped water blast and pearls of spray
2. EnemyTideFollower — rolled green-grey tide splash carrying several seaweed flecks
3. EnemyWreckScavenger — rusted curved salvage hook swinging through golden-green sparks
4. EnemyMooringWarden — taut dark bronze mooring chain pulling diagonal knot and sparks
5. EnemyOathlineGuard — forest green lacquered shield-crash splinters flashing jade
6. EnemyRearHunter — two abrupt narrow cold silver knife slashes with charcoal shadow
7. EnemyBellMagistrate — bronze bell ripple with ornate scalloped waves
8. EnemyShipCaulker — warm muddy mallet strike with boards, oak splinters and dust
9. EnemyForkFisher — sea-green three-pronged harpoon wake with dark seafoam
10. EnemyFogAngler — ghostly yellow-green deep-sea bubbles with murky bioluminescence
11. EnemyLanternMoth — amber moth scale flakes and luminous ragged wing dust
12. EnemyCrackedPotter — orange earthenware shard explosion and glaze dust
13. EnemyAshLanternAdept — lavender lantern fire gust with grey ash and pinprick embers
14. EnemyRoadBrute — heavy grey-brown flail shock of gnarly blunt chunks
15. EnemyBlockadeAdmiral — large orange naval cannon fireball with soot and chipped iron
16. EnemyDrownedLanternRay — royal blue broad fin-sweep pressure wave with electric droplets

## Graveyard-workers

1. EnemyGraveDigger — rusty spade swing with clods of cemetery dirt, dry grass and copper flash
2. EnemyGraveShoveler — half-ring of mossy green corpse haze and clinging dust, pale olive interior
3. EnemyTombShield — wide slate gravestone bash, blue-white mineral fragments
4. EnemyRustSpear — sawtooth thrust with rusty steel sparks and dry thorn offshoots
5. EnemyVigilCrossbow — long yellow skeletal barbed crossbow bolt with fine ivory shards
6. EnemyBoneLantern — ghost green lantern fire plume with bright pale licks and bluish sparks
7. EnemyDirgeAcolyte — magenta-purple overlapping ripples and ragged prayer ribbons
8. EnemyStitchServant — flicking crimson suture needle with a circular red loop
9. EnemyShroudWeaver — slashing soft grey-purple frayed silk strips
10. EnemyGraveFern — broad toxic lime curling fern stalks and luminous spores
11. EnemyEpitaphGargoyle — stone lilac occult stone plate breaking into thick olive shards
12. EnemyCarrionRaven — frantic dark slate feather flurry with teal highlights
13. EnemyBoneHyena — curving pale ivory jawbone rake, repeated teeth and brown dust
14. EnemyPhosphorWisp — azure ghost fire with cyan phosphor sparks
15. EnemyFrostRevenant — sharp royal blue frost spray and long white glittering icicles
16. EnemyBoneNun — vintage ivory votive flame halo and pearl fragments

## Graveyard-night

1. EnemyCoffinHound — amber skeletal hound bite with splintered claws and slate fog
2. EnemySplitCoffin — fan of dark wood coffin fragments with dusty burgundy shrapnel
3. EnemyRustChain — rusty dark orange chain links half looping with flying iron flakes
4. EnemyFuneralDrummer — low sweeping rose funeral drum resonance ripples
5. EnemyMistWraith — huge grey indigo smoky wraith tendril with cold gleam
6. EnemyBonePeddler — tarnished green brass coins and bone dust, no symbols or letters
7. EnemyPallbearer — dark turquoise low coffin-shaped slam trailing chipped slate fragments
8. EnemyGraveBell — plunging ochre brass-weight shock ring with stout loose fragments and short gold sparks
9. EnemySoulStitcher — peach-violet curved spectral threads weaving through heated steel needle glints
10. EnemyWeepingGargoyle — moon-blue stony fist and frozen water streaming in broad bands
11. EnemyGraveExecutioner — long red-copper headsman's axe sweep with halo of chipped rust
12. EnemyNamelessKing — antique-gold crescent cleave dissolving into regal black-gold dry ash
13. EnemySepulchralMatron — cold ivory-green overflowing sacred casket light and pearl particles
14. Chant — pink and warm ivory fluttering musical air swirls without literal marks, faint flower-shaped light inside
15. RelicHunter — two amber bronze relic hooks and molten copper streak
16. IntelligenceOfficer — whipping lilac pages and violet quill streaks, no letters

## Capital-machines

1. EnemyGatePunch — brass ticket-stamp impact with tiny punched copper discs and angular sparks
2. EnemyMailSphere — capsule of brass and paper with curled gold slips streaming, no text
3. EnemyPorcelainConductor — white faded blue porcelain shards and conductor baton flash
4. EnemyLensSurveyor — rectilinear green precision ray with rings of refracted glass
5. EnemyRivetMantis — teal steel rivets in a serrated line with bursts of hot metal
6. EnemyShearCentipede — parallel brass iron shear cuts with black angular sparks
7. EnemyWalkingKiln — earthy orange fire gust belching coal fragments
8. EnemyValveMedic — turbulent mint steam vent shearing diagonally with bright thin edges
9. EnemyFlueSweep — black chimney-soot streamer and dark orange cinders
10. EnemyLuggageCrane — medium antique brass curved crane hook and twisted cable
11. EnemyBogieHound — rolling oily iron wheel with dull green rim teeth and railway sparks
12. EnemyFareInspector — fan of weathered cinnamon ticket-shards sweeping with copper flash, no lettering
13. EnemyWeldFirefly — blinding white-yellow tight welding arc and golden short sparks
14. EnemyCabinetMaw — brown dark veneer splinters with burgundy lit edges and snapping wood
15. EnemyCokeBoar — dark red incandescent coke chunks blasted behind a ram-shaped flare
16. EnemyPhonoHerald — short warm ochre nested gramophone air cones, dark brass wisps

## Capital-night

1. EnemyKilnTender — bright warm narrow vertical oven flame curve, glowing brick chips
2. EnemyDieBoxer — steel hydraulic fist-strike shock cone with copper and grease flecks
3. EnemyGlassGardener — whipping sage leaves and teal broken glass slivers, water sparks
4. EnemyCivicNurse — white mint needle with tiny porcelain ampoule shards
5. EnemyPostmaster — whipping fan of metallic amber mechanical feathers with burning paper crumbs, no letters
6. EnemyArsonExaminer — stinging rich blue flame with burning ice-white nucleus
7. EnemyFilingJustice — three silver-purple gavel chains lashing across splintered wax seals, no lettering
8. EnemyPressureBishop — turquoise high-pressure steam dome with gold brass rim particles
9. EnemySwitchMarshal — two diagonal desaturated yellow signal-blade wings with intersecting sparks
10. EnemyTurbineKnight — fast spinning cobalt bladed turbine in a white rim of vapour
11. EnemyAstralGovernor — a star orbit of pale blue shimmer with gold dust halo and violet thin line
12. EnemyCrucibleRegent — broad gold lava spray with jagged orange molten droplet edges
13. EnemyRoyalEngine — long red and gold rail wheel glow with large brass spokes and short axle flares
14. EnemyDeckSailor — incandescent musket ball and short orange sparks
15. EnemyThreadRifleman — long cyan thread bullet with narrow silver coils
16. EnemyCorpseCrossbowman — dull ivory iron skeleton bolt spinning along thin muddy dark-green mist

## 独立补绘

- 铜铃庸医用破裂的琥珀色药瓶、独立的橄榄绿药液和悬浮液滴。
- 盐壳蟹用交错的珊瑚红双钳、盐晶和白色擦击残迹。
- 电键调度员用青白色分岔电弧与两个发光的铜接点。

这三张图分别生成，要求边缘及飞溅物之间保持透明，画面不带底板、地面和灰白雾块。
