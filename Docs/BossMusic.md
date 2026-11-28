# 首领战配乐

正式讨伐和独立演练共用同一套首领战音乐。每首按首领的固定 ID 寻址，因此更换地区、阵容预设或首领随从时，会播放实际参战首领的曲目。

| 地区 | 首领 | 曲名 | 编配 | 循环 |
|---|---|---|---|---:|
| 弥雾码头 | 封港提督 | Cannonwatch | 低铜管、短弓弦乐和军鼓，采用 D 小调进行曲 | 61.94 秒 |
| 弥雾码头 | 沉灯巨魟 | Light Below the Wake | 玻璃琴、长笛、合唱与深低音，采用 6/8 拍 | 53.33 秒 |
| 低语墓园 | 无名葬王 | The Unnamed Sentence | 管风琴、低铜管、合唱与低钟，采用升 C 小调 | 71.11 秒 |
| 低语墓园 | 守墓圣母 | Vigil of Ash | 钢琴、长笛和合唱，采用 G 小调三拍挽歌 | 50.53 秒 |
| 遗都 | 末班皇车 | No Station Beyond | 铜管、断奏弦乐、机械低音和蒸汽打击声 | 52.60 秒 |
| 遗都 | 天球摄政 | The Fault in the Meridian | 玻璃琴、钢琴与钟表打击声，采用 7/8 拍 | 56.47 秒 |
| 遗都 | 熔心执政官 | Redline Crucible | 断奏弦乐、低铜管、铁砧和机械低音 | 56.47 秒 |

## 联调

`BattlePrototype` 在正式开战后，把敌方首领的 ID 交给 `GameAudio`。后者从 `Resources/BossMusic/<首领 ID>` 载入曲目，沿用区域环境的循环声道和淡变逻辑。Boss 曲目的声道增益为 0.34，并应用主音量与环境音量设置。战斗音效通过原有的独立声道播放，入场提示和技能命中音效照常触发。

布阵阶段持续播放区域环境声。战斗结束进入谢幕、重置演练或离开场景时，解除 Boss 曲目并恢复当前场景的音轨。缺少匹配文件时沿用区域环境声。音乐文件使用流式载入，由 Git LFS 管理。

## 制作与验证

曲目由 [ComposeBossMusic.py](../Tools/ComposeBossMusic.py) 中的原创乐谱和合成器生成。依赖 NumPy、SciPy 与支持 `libvorbis` 的 FFmpeg。运行 `python Tools/ComposeBossMusic.py` 可重建 48 kHz 双声道 Ogg 文件。合成器把跨越循环终点的音符与混响尾部叠回开头，循环以乐句长度对齐。保留与同名音频文件对应的 Unity `.meta`，即可沿用已有的导入参数和 GUID。

`EveryBossFormationPlaysItsOwnLoopAndRestoresSceneAmbience` 通过三个独立演练场景的敌方阵容界面逐个开战，核对七个首领的曲目、流式载入和恢复环境声。正式讨伐走同一个开战入口。
