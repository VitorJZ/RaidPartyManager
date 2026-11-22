# 敌人立绘与战斗动画

敌人资源位于 `Assets/RaidPartyManager/Art/Enemies/`。图集朝左，适用于战场右侧；每帧均使用透明底和统一脚底线。

| 后缀 | 排列与用途 |
| --- | --- |
| `Portrait.jpg` | 竖幅立绘 |
| `Combat.png` | 16 列 × 4 行，依次为待机、普通攻击、受伤、倒地。每行 16 帧。 |
| `Attack.png` | 4 列 × 4 行，宽幅普通攻击，按行读取。 |
| `Walk.png` | 4 列 × 4 行，16 帧步态，用于入场、近战接近与回位。 |
| `Special.png` | 4 列 × 4 行，角色特有动作。 |
| `SpecialFX.png` | 16 列 × 1 行，从生效帧起播放的目标或自身效果。 |
| `FinaleFX.png` | 敲钟判官第三声的钟波，16 列 × 1 行。 |
| `TrueStrikeFX.png` | 铜铃庸医的普攻命中反馈，16 列 × 1 行。 |
| `ThreadRifleman-Projectile.png` | 拨线枪手用的火枪弹道。 |

码头演练包含原有九名和后续补充的十四名敌人。拨线枪手、灰灯术徒和收尸弩手以原战斗像素图渲染 16 帧步态，入场时使用各自职业的步幅。普通攻击和特有动作按各职业的关键动作时间进入命中或施放姿势，远程攻击的生效时机是投射物到达目标时。

| 资源名前缀 | 敌人 | 特殊动作与反馈 |
| --- | --- | --- |
| `ThreadRifleman` | 拨线枪手 | 火枪后座 |
| `AshLanternAdept` | 灰灯术徒 | 抬灯与推杖施法 |
| `RoadBrute` | 拦路蛮兵 | 咆哮抬拳时表现 3 层盛气 |
| `CrackedPotter` | 裂陶术士 | 同次命中后的陶片继续向后排飞散 |
| `BellMagistrate` | 敲钟判官 | 槌头碰钟累计次数，第三次触发大钟波 |
| `RearHunter` | 绕后猎手 | 低身切向已选队尾目标，再双刃挥击 |
| `OathlineGuard` | 誓线护卫 | 盾脚落地时表现 1 层坚毅 |
| `CorpseCrossbowman` | 收尸弩手 | 重拉弦、红色弩矢和更重的后座 |
| `BrassQuack` | 铜铃庸医 | 治疗铃波落在受治疗单位；细刃刺击用银紫真实伤害反馈 |

图鉴留空的战斗细节在演练中使用以下暂定值。

- 收尸弩手未抽到攻击时待机。
- 铜铃庸医恢复 4 点生命，优先选择生命比例最低且可治疗的受伤敌方单位，包括自己。没有可治疗目标时直接攻击。
- 裂陶术士的陶片打向主目标后第一名存活单位，空位跳过。敲钟判官每场战斗只在第三次敲钟时触发一次钟波。

## 低语墓园

新增 30 名敌人，每名各有透明立绘、待机、移动、普通攻击、特技、受击和倒下动作。素材位于 `Art/Enemies/WhisperingGraveyard/`，原始 ImageGen 8×6 姿势图保存在 `Sources/`。参考已有敌人的像素绘制风格，每名独立生成。提示词见 `WhisperingGraveyardArtPrompts.json`，逐名预览见[墓园图鉴](WhisperingGraveyard.md)。

`Tools/BuildWhisperingGraveyard.py` 从原画关键姿势生成运行时图集。Combat 为 3072×768，Attack 与 Special 为 1024×768，Walk 为 768×768，SpecialFX 为 2048×128。28 名敌人的粒子按动作和状态绘制。无名葬王与守墓圣母使用独立生成的透明王冠和圣辉帷幕序列。

配置行为的第二项及后续招式用 `GraveyardSpecial` 选择特技姿势；蓄力用 `GraveyardCharge`，状态栏显示一行动倒计时。半血阶段用 `GraveyardPhase` 发出一次效果事件。所有结算由模拟器决定，图集播放不改变目标或数值。`GraveyardPlaybackTests` 检查墓园背景和实际动作层中的逐帧图片。

## 补帧与校验

`Tools/AnimationCels.py` 用双向光流补全原有关键姿势，对透明像素使用预乘 Alpha。行走与呼吸会从末帧回到首帧，倒地和开箱会停在最终姿势。重制旧图集时，从项目根目录向脚本传入一份独立的旧版 Art 目录。墓园角色可从保留的原始姿势表运行 `Tools/BuildWhisperingGraveyard.py` 重建。两位首领的彩绘命中特效保存在各自的 SpecialFX PNG 中。

无名葬王的原画提示为“An empty jagged obsidian crown rises out of a turquoise and violet spectral seal, fractures outward into large pale soul splinters then recedes to a last wisp.” 守墓圣母的原画提示为“A frosted silver reliquary halo unfurls in flowing ribs like a votive veil, sheds ice-blue falling tears and small amber votive lights then dissolves.” 两张原画均由内置 ImageGen 按 4×4 透明图集生成，导入时重新排列为 16×1。
