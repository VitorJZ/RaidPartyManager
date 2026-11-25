# ElevenLabs 音效补录

清单来自《待补音效与ElevenLabs提示词.xlsx》的「待补音效」表，第 7–134 行。[JSON 清单](ElevenLabsSoundEffects.json)保留原提示词、秒数、循环设置、建议文件名和播放时机。共 128 条，含 9 条循环环境音，请求时长合计 289.1 秒。旧的 90 条 Sonilo 音效继续使用原有文件和映射。

## Unity 接入

项目内的成品位于 `Assets/RaidPartyManager/Audio/ElevenLabs/`，由 Git LFS 管理。128 条成品均为 48 kHz、16 位 PCM、立体声，实际时长合计约 288.26 秒。以清单文件名为准导入，Chrome 的原始下载名和重复副本保存在 `Logs/`。现有 90 个音效索引继续使用，ElevenLabs 按清单顺序追加到 `SoundCue` 和 `MistboundSounds.asset` 的第 91–218 号槽。第 0 号槽保留为静音。

- **环境（9 条）**。主菜单使用行旅循环。低语墓园、机巧遗都分别在选路、战斗与室外事件、城镇、商店、人才市场切换对应的环境循环。两区域的独立战斗场景也可听到各自的环境声。环境使用 `AudioSource.loop` 和流式导入，沿用音量菜单和淡入淡出。
- **舞台、角色和通用提醒（11 条）**。开闭幕、翻台、路牌落地、合同提醒、深海轻语的乘浪、落地和水刃，以及种子落下、发芽、花匠摘花，都在对应的舞台或动作回放位置播放。
- **战斗（77 条）**。包括五位首领的两段入场音、14 条首领机制、31 条装备触发、18 条技能和 4 条状态或传毒音。用装备类型、战斗事件和状态差量选声效。铁壁接管在真正发生拦截时播放。雷暴裁决在释放回合按施法者播放一次。疾风手套在追加攻击前发出触发声。普通受击、治疗和早期技能继续按已有流程播放。
- **事件（31 条）**。按实际确认的选择、演出动作段和特殊场景的位置播放。墓钟按选择敲一次或三次，双人床位分别播放上床和确认生命移交的声音，拍卖的匣盖声跟随开盖帧。遗都的路灯、乐团和圆顶校准分别在点亮、留下听演出、选择校准时触发。

新增录音请同时追加清单、对应 WAV 和 `.meta`、声音枚举及银行引用。`SoundBankTests.ImportedClipsMatchManifestAndKeepLegacyBankSlots` 用 Unity 检查每条资产的路径、时长、循环导入方式和银行槽位。技术校验覆盖 PCM 格式、非静音和时长，音色、混音和九条循环接缝仍以试玩试听为准。

2026-11-26 使用 Unity 6000.6.9f1 验证。全量 Edit Mode 通过 332 项，选跑的 5 项 Play Mode 全部通过，覆盖三个场景间的环境音切换、首领入场、护符保护、机巧遗都事件与终幕。首次 Edit Mode 的 3 项贴图失败在恢复 35 个已跟踪的 LFS 文件后通过。

## 用常用 Chrome 的 Tampermonkey 批量生成

运行 `py -3.11 Tools/BuildElevenLabsUserscript.py`，把 128 条清单打入 `Logs/ElevenLabsUserscriptInstall/ElevenLabsSfx.user.js`。在 Chrome 的 Tampermonkey 管理面板导入该文件。新版 Chrome 如提示未赋权，请在 Tampermonkey 扩展详情手动开启「允许运行用户脚本」；不需要开启开发者模式。脚本只匹配 `elevenlabs.io/app/sound-effects` 下的目标页面，不读取登录凭证。

打开已登录的 ElevenLabs「音效 → 历史」，确认网页显示不会分享到 Explore。在 Chrome 的下载设置和「讨伐队经理 · 音效批量」面板中同时选择项目的 `Logs/ElevenLabsSoundEffects`。自动运行时关闭 Chrome 的「下载前询问每个文件的保存位置」。已有音效时可先点「仅补下载」验证，再点「开始批量」。积分上限留空时使用当次网页余额，脚本不会购买积分或升级。浏览器只需取得这个音效目录的读写权限。

已有 WAV 会被验证并跳过。每次提交前，本地进度会记下清单条目，重开页面后可以重新选择同一目录续跑。如果网页没有确认结果，重试只查历史和下载，不会重复点击生成。脚本优先保存原生 WAV。循环音效的 WAV 选项不可用时，脚本将网站的 MP3 解码为 WAV，循环接缝需要试听。

面板上的「检查模型」只读取当前模型，不消耗积分。版本 1.0.1 兼容网页显示的 `v3` 和模型标识 `eleven_text_to_sound_v3`，避免控件内部值让脚本误判。

版本 1.0.3 分开显示生成、历史核对、下载和保存。墓园室外音效已出现在历史中时，旧版仍等待生成，因为它把识别候选下载按钮也当作生成完成的条件。新版识别完整提示词或可唯一匹配的省略文本，再从历史的第一个候选读取编号和时长。WAV 校验通过并写入输出目录后，批量模式才会处理下一条。单条测试保存后结束。「仅补下载」只处理历史中已有的音效，不消耗生成积分。

版本 1.0.4 用指针按下事件打开历史候选的下载菜单。墓园聚落音效已经生成且可手动下载时，旧版的 `.click()` 没有触发菜单，最终报「等待下载格式超时」。新版只读取当前候选的菜单，报错时会区分菜单打不开和菜单里没有可用格式。

版本 1.0.5 直接检查 Chrome 下载到所选目录的文件。墓园商店音效已下载时，旧版仍在等待网页音频事件，导致超时。脚本现在用提示词文件名前缀和候选编号 `#1` 识别原始文件，核对音频格式和时长后按清单命名保存，并在进度中记录原始文件名。目录中已有唯一匹配的原始文件时会直接复用。可把已下载的原始文件放进项目音效目录，再用「仅补下载」继续。原始下载会保留在目录中。

## 用 Chrome 网页批量生成（无需 API 密钥）

需要本机已有 Google Chrome，安装 Python 的 Playwright 包即可。循环音效需要 PATH 中有 FFmpeg。这台 Windows 的 `python` 指向没有 pip 的 MSYS2 版本，所以这里使用已安装的 `py -3.11`。脚本使用独立的 Chrome 配置目录，首次需手动登录自己的 ElevenLabs 账号。在打开的「音效」页面关闭 Explore 分享、选用 v3，然后回到终端按回车。以后复用此配置的登录状态。

```powershell
py -3.11 -m pip install "playwright>=1.51,<2"
py -3.11 -X utf8 Tools/GenerateElevenLabsSfxChrome.py --setup

# 查看未完成文件，不发请求
py -3.11 -X utf8 Tools/GenerateElevenLabsSfxChrome.py

# 生成一条短音效验证
py -3.11 -X utf8 Tools/GenerateElevenLabsSfxChrome.py --generate --only stage_stone_sign_land

# 继续处理剩余文件；也可加 --limit 5 或 --max-credits 200
py -3.11 -X utf8 Tools/GenerateElevenLabsSfxChrome.py --generate
```

每个提示词只点一次「生成音效」。网页版会返回四个候选，脚本下载与该提示词匹配的第一个。普通音效直接保存网页提供的 WAV；循环音效下载 MP3 后转为 WAV，保留声道和接缝，不做裁切。成品位于 `Logs/ElevenLabsSoundEffects/`，包括已下载的合同到期样音；再次运行会校验并跳过已有成品。

脚本只使用页面上显示的积分余额，余额不足、无法确认分享已关闭、无法确认生成结果或下载选项时停止。重跑会优先使用 `.chrome-progress.json` 中确认过的历史链接；如果上次提交后没有拿到链接，会搜索历史，只尝试下载，避免重复扣费。历史中确实没有对应生成时，请先在 Chrome 手动核对；可以手动生成并按清单命名保存，再运行批次继续。批次应单实例运行。

## 用官方 API 批量生成

在仓库根目录运行，使用 Python 3.10 或更高版本，生成 WAV 时需要 PATH 中有 FFmpeg。请在本机配置有 Sound Effects 权限的 `ELEVENLABS_API_KEY` 环境变量。Windows 用户变量可直接由脚本读取；无需将密钥写到文件或聊天中。

```powershell
# 预览批次：不请求 API，不消耗额度
python -X utf8 Tools/GenerateElevenLabsSfx.py

# 先试听一条短音效和一条循环环境音
python -X utf8 Tools/GenerateElevenLabsSfx.py --generate --only ui_contract_expiry_warning --only amb_main_menu_march

# 生成全批；已验证的 WAV 会跳过
python -X utf8 Tools/GenerateElevenLabsSfx.py --generate
```

脚本默认使用 [ElevenLabs Sound Effects API](https://elevenlabs.io/docs/api-reference/text-to-sound-effects/convert) 的 SFX v3，只在环境音条目传 `loop: true`。如果 API 明确拒绝 v3 循环，可单独用 `--loop-model v2` 重跑该类条目。请求使用通用 MP3 格式，再转成 44.1 kHz PCM16 WAV，保留声道；循环素材不额外裁切或加淡入淡出。转成 WAV 不会恢复压缩中丢失的细节。

生成目录为 `Logs/ElevenLabsSoundEffects/`，可用 `--output` 指定其他目录。每条生成后校验 WAV 格式、波形和时长，通过后才写入最终文件名。服务返回的 MP3 缓存在 `.source`，转换中断可复用。API 请求出错时停止，避免自动重发可能计费的请求。更新 Unity 素材请按上方接入步骤处理并复测。
