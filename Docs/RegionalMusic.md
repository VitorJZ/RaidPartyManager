# 地区与场景配乐

弥雾码头、低语墓园、遗都各有六首原创循环曲。每个地区使用一组音色和主题，进入设施时换成该设施的曲目。

| 场景 | 弥雾码头 | 低语墓园 | 遗都 |
|---|---|---|---|
| 探索 | Across the Mooring Lights | Footprints Past the Yews | An Hour Above the Rails |
| 普通和精英战斗 | Cut the Mooring Lines | The Earth Gives Way | Platforms in Revolt |
| 城镇 | Lanterns on the Quay | Candles at the Gatehouse | The Hour Opens |
| 商店 | The Tackle Drawer | The Keepsake Cabinet | A Counter Full of Gears |
| 人才市场 | A Berth for Hire | Names for the Crossing | Applications at Platform Four |
| 营地 | One Lamp Left Afloat | The Quiet Watch | Warmth in the Service Alcove |

码头用长笛、拨弦和铜管表现水上的空旷感和登船战斗的节奏。墓园以低钟、合唱和管风琴为主，城镇和营地换成更轻的织体。遗都以钟表打击声和机械低音贯穿，战斗采用七拍分组。

## 在游戏中播放

`GameAudio` 根据实际远征所在楼层或演练场景名确定地区。共用的城镇、商店、人才市场和营地场景从当前远征取地区。地图、户外事件和战前准备使用探索曲。招募转盘事件沿用户所处地区的户外场景播放探索曲。

正式开战时，普通战和精英战切到地区战斗曲，首领战优先按敌方首领 ID 读取[首领专属曲目](BossMusic.md)。战斗谢幕和重置会恢复当前场景配乐。所有曲目沿用主音量及环境音量设置，与战斗音效在各自的声道上播放。

## 资源与验证

`Assets/RaidPartyManager/Audio/Resources/RegionalMusic` 内的曲目以 `地区-场景.ogg` 命名。运行 [ComposeRegionalMusic.py](../Tools/ComposeRegionalMusic.py) 可重建曲目，依赖 NumPy、SciPy 和支持 `libvorbis` 的 FFmpeg。乐谱包含 24 小节的三个段落，录音为 48 kHz 双声道 Ogg，Unity 使用流式播放。重建时保留原 Unity `.meta`，保留资源 GUID。

`SharedFacilitiesPlayTheActiveRegionsMusic` 逐个验证三地的探索和四类共用设施。`EveryBossFormationPlaysItsOwnLoopAndRestoresSceneAmbience` 验证三地普通战、七个首领战以及重置后的音乐。
