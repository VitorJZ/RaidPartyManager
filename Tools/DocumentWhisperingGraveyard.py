"""Build the enemy design notes and optionally copy the finished pack to Obsidian.

Run after BuildWhisperingGraveyard.py and the Unity balance test. No network or Git
operations. --sync-obsidian adds a dedicated directory to the existing synced vault.
"""
from __future__ import annotations
import argparse
import csv
import json
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VAULT = Path("C:/Users/zhang/OneDrive/讨伐队经理 Obsidian/讨伐队经理")
ART = ROOT / "Assets/RaidPartyManager/Art/Enemies/WhisperingGraveyard"
PREVIEWS = ROOT / "Docs/Previews/WhisperingGraveyard"
RANK = {"Normal": "普通", "Elite": "精英", "Boss": "Boss"}
PROFESSION = {"Warrior": "战士", "Mage": "法师", "Archer": "射手", "Support": "后勤", "Monster": "怪物"}
TARGET = {"Front": "最前方存活对手", "Rear": "最后方存活对手", "LowestHealth": "生命比例最低的存活对手",
          "HighestAttack": "攻击最高的可选对手", "LowestDefense": "防御最低的可选对手", "Self": "自身",
          "WoundedAlly": "生命比例最低的存活友方", "FrontTwo": "最前两名可选对手", "RearTwo": "最后两名可选对手",
          "AllEnemies": "所有存活对手", "AllAllies": "所有存活友方", "StrongestAlly": "攻击最高的存活友方"}
STATUS = {"Vigor": "盛气", "Swift": "迅捷", "Resolve": "坚毅", "Ember": "余烬", "Poison": "中毒",
          "Exposed": "破绽", "Weakened": "萎靡", "Stunned": "眩晕", "Injured": "受伤", "Cursed": "诅咒", "Chill": "寒冷"}
DAMAGE = {"Physical": "物理", "Magic": "魔法", "Fire": "火焰"}
APPEARANCE = {
    "GraveDigger": "佝偻的亡灵掘墓工，棕色破兜帽下露出青绿眼光，拖着锈铲，腰间挂着偷来的殉葬钱袋。",
    "GraveShoveler": "墓苔覆盖的肥大尸骸，铁锹与一侧前臂长在一起，肋骨间冒出淡绿墓菌。",
    "TombShield": "举着裂纹墓碑的骷髅守卫，凹陷铁盔下缠着深蓝裹尸布。",
    "RustSpear": "瘦削的骸骨枪兵，暗红战袍破碎，胸骨与长枪缠满锈色荆棘。",
    "VigilCrossbow": "披黑色破斗篷的骷髅哨兵，独眼泛黄，背着沉重墓弩和骨矢。",
    "BoneLantern": "高瘦的提灯骷髅，蓝绿葬袍垂落，弯杖悬挂青色魂灯与送葬飘带。",
    "DirgeAcolyte": "戴面具的幽灵唱诗学徒，嘴上有蜡封，手持紫色破歌本和银色音叉。",
    "StitchServant": "矮小的缝合尸仆，沾污麻布围裙，背负红线轴，挥动巨大的弯缝针。",
    "ShroudWeaver": "蒙面的幽魂织女，苍白长指牵引浮空纺锤，靛青尸衣上缠着细银丝。",
    "GraveFern": "寄生在肋骨上的爬行墓蕨，根须化腿，卷曲紫色叶片托着幽绿孢子囊。",
    "EpitaphGargoyle": "矮壮的花岗岩石像兽，背翼折断，牙缝嵌着墓志碑片，石皮上生着墓苔。",
    "CarrionRaven": "骨喙黑羽的巨大渡鸦，眼中泛绿，一条腿系着葬带，背羽像破碎的荆棘巢。",
    "BoneHyena": "枯瘦的墓地鬣犬，肩部露出白骨甲片，前爪呈铲形，铁项圈拖着短链。",
    "PhosphorWisp": "围绕裂颅漂浮的青色磷火，下方悬挂小铜香炉，紫火丝随移动飘散。",
    "FrostRevenant": "冻裂尸布包裹的亡灵，杖头像结冰的铁锹，蓝白骨骼间缠着冻土根须。",
    "BoneNun": "眼窝空洞的亡灵修女，象牙白与午夜蓝修服褪色，手持指骨念珠和银色小灯。",
    "CoffinHound": "披棺木甲片的壮硕尸獒，铁口笼、断裂葬铃与粗铁链构成沉重轮廓。",
    "SplitCoffin": "被裂开的立棺困住的巨大行尸，长钉穿臂，拆下的棺盖成为沉重木槌。",
    "RustChain": "无面墓狱看守，锈铁兜罩住头部，皮围裙挂着墓钥，挥动巨大的链钩。",
    "FuneralDrummer": "身披破紫色礼服的骷髅鼓手，用肋骨鼓槌敲击胸前旧鼓，银灰流苏随鼓点摆动。",
    "WaxAttendant": "细瘦的白蜡侍者，蜡冠缓缓熔化，举着黄铜灭烛器，头顶烛火泛出琥珀光。",
    "MistWraith": "长颌怨灵裹着烟青色半透明丧服，空洞喉咙泛紫，指爪与面纱拖入雾中。",
    "BonePeddler": "驼背的骷髅商贩披苔绿斗篷，背架挂满小骨与旧钱，手中的破天平用来称量赎骨。",
    "Pallbearer": "四名小骷髅在铁箍华棺下连成一个整体，八条手臂协同行动，棺缝溢出青色魂光。",
    "GraveBell": "空壳巨甲中的墓钟看守，拖着一口开裂铜钟，木槌、铜绿甲片与靛青布条一同摇摆。",
    "SoulStitcher": "戴象牙鸟面具的高瘦殓医，手持关节针杖，红色魂线漂浮在深青长衣周围。",
    "WeepingGargoyle": "跪伏的巨大墓园石像，背负一片哭墙，石脸流出青蓝光泪，锯齿石翼蜷在身后。",
    "GraveExecutioner": "黑紫兜袍下的宽肩刽子手，挥动新月形巨斧，墓牌与铁钥链随巡行撞响。",
    "NamelessKing": "破碎黑曜冠下是一张无面的银色死面具，葬王持残缺巨剑，黑色王袍缝隙中游动着青色魂火。",
    "SepulchralMatron": "巨大的哀悼圣母披着象牙白与深蓝葬袍，瓷质死面具上悬着墓钥光环，长手托住装满冷魂灯的摇篮圣匣。"
}


def effect(action: dict) -> str:
    kind = action["kind"]
    if kind == "Attack":
        power = action.get("power", 0)
        text = f'{DAMAGE[action.get("damage", "Physical")]}攻击，攻击修正 {power:+d}'
        if "status" in action:
            text += f'；命中且目标存活后附加 {action["layers"]} 层{STATUS[action["status"]]}'
        return text
    if kind == "Heal": return f'恢复 {action["power"]} 点生命，仅选择受伤且未被诅咒的存活友方'
    if kind == "Shield": return f'增加 {action["power"]} 点护盾，可保护满血友方'
    return f'施加 {action["layers"]} 层{STATUS[action["status"]]}'


def actions_table(enemy: dict) -> str:
    if enemy.get("mechanic"):
        return enemy["mechanic"]
    lines = ["| 行动 | 自由行动抽取概率 | 目标 | 效果 | 时机 |", "| --- | --- | --- | --- | --- |"]
    for action in enemy["actions"]:
        timing = "先蓄力，下次可行动时释放" if action.get("charge") else "本次行动"
        lines.append(f'| {action["name"]} | {action["weight"]}% | {TARGET[action["target"]]} | {effect(action)} | {timing} |')
    return "\n".join(lines)


def doc(enemies: list[dict]) -> str:
    lines = ["# 低语墓园", "", "2026-11-19 · 第 2 层区域 · 23 普通、5 精英、2 Boss", "",
             "设计依据是同步 Obsidian 的《区域图鉴》《敌人图鉴》《遭遇与难度》。原区域效果留空，本次继续留空。敌人生命、招式与视觉均为本次新增的初始配置。", "",
             "## 试玩入口", "", "通关码头后进入第二层低语墓园。在编辑器中可打开 `Assets/Scenes/WhisperingGraveyard.unity`。设置中的「敌方编辑」包含这 30 名敌人，可用来搭配普通、精英和 Boss 遭遇。角色编辑、装备背包、回合间换位、结算和重置沿用现有流程。", "",
             "墓园和码头使用独立的敌阵缓存与敌人池。每次随机开局从三套普通预设中抽取；在「设置 → 敌方编辑」可载入全部 10 套预设（3 普通、5 精英、2 首领），也可以继续手动换位、换怪。正式讨伐仍使用码头路线。预设名单见[敌阵预设](EnemyFormations.md)。", "",
             "## 规则与难度", "",
             "普通敌人攻击 4–6、生命 16–26。精英和 Boss 的加强数值见下方清单，以 JSON 配置源为准。精英占 2 格，Boss 占 3 格，随从加主单位总计最多 4 格。攻击修正先加到攻击，再经过现有防御、特防、闪避、状态与装备伤害链。", "",
             "自由行动按权重抽取一个招式。蓄力占一次行动，下次可行动时释放同一招式，眩晕会推迟。倒计时显示在单位状态栏。目标按释放时的存活者与站位重选，同分按站位从前到后。单体与前后二人技能遵守现有嘲讽和潜行可选目标规则，整体群攻作用于所有存活对手。", "",
             "治疗可选自身，但排除倒下、满血和被诅咒的友方。无合法治疗目标时普攻。护盾可以保护满血友方。新附加的诅咒、萎靡、眩晕跳过第一次回合末递减，避免敌方后行动造成效果立即消失。其余状态沿用现有规则。", "",
             "两名 Boss 在生命不高于 50% 后首次可行动时进入第二阶段，伤害招式的攻击修正永久增加 1，只触发一次，不增加治疗、护盾和状态层数。", "",
             "普通遭遇建议至多一个治疗者和一个眩晕来源；精英可带一至两名普通随从，Boss 可带一名普通战士。预设覆盖原难度试算的阵容，手动编辑仍允许自由混编。", "",
             "## 敌人清单", "", "| 序号 | 敌人 | 级别 | 职业 | 攻／防／特防／血 | 占格 |", "| --- | --- | --- | --- | --- | --- |"]
    for e in enemies:
        lines.append(f'| {e["number"]} | [{e["name"]}](#{e["name"]}) | {RANK[e["rank"]]} | {PROFESSION[e["profession"]]} | {e["attack"]}／{e["defense"]}／{e["specialDefense"]}／{e["health"]} | {e["slots"]} |')
    lines += ["", "![立绘总览](Previews/WhisperingGraveyard/Roster.jpg)", "", "## 行为与应对", ""]
    for e in enemies:
        lines += [f'### {e["name"]}', "", APPEARANCE[e["id"]], "", actions_table(e), "", f'应对：{e["counter"]}', ""]
        if e.get("openingShield"): lines += [f'开场获得 {e["openingShield"]} 点护盾，每场一次。', ""]
        lines += [f'[六类动作预览](Previews/WhisperingGraveyard/{e["id"]}.gif)', ""]
    lines += ["## 数据与美术制作", "",
              "`Docs/WhisperingGraveyard.json` 是本批配置源。`EnemyBehaviorDefinition` 只包含纯数据，`EnemyBehaviorSpec` 从 Unity 资源转换，`ConfiguredEnemyActions` 负责随机、目标、蓄力和伤害结算。`CharacterDefinition` 新增可选的行为配置，已有敌人继续使用原行为。枚举新值只追加。", "",
              "内置 ImageGen 为每个敌人独立生成透明 8×6 原图，参考现有敌人像素绘制。原图每行 8 帧，依次为待机、移动、普攻、特技、受击、倒下。`Sources/` 保留生成结果，提示词见 `WhisperingGraveyardArtPrompts.json`。", "",
              "`python Tools/BuildWhisperingGraveyard.py` 将原画按格拆分并补间。待机、移动、普攻、特技、受击、倒下及命中特效均为 16 帧。攻击与特技按原有动作时间生效，首领使用专属彩绘特效，回放时钟与暂停、倍速共用。美术资源、定义和 .meta 一并保留，GUID 可重复生成。", "",
              "## 修改与交接", "",
              "数值与行动先修改 JSON，再运行 `python Tools/BuildWhisperingGraveyard.py --data-only` 更新 Unity 定义。美术替换保留同一 ID 的原图文件名，运行完整装配命令并检查 GIF。行为扩展先调整 Domain 契约和 Content 转换，再实现 Battle 结算，最后更新相关测试。", "",
              "已依据用户提供的本地图鉴完成合并，完整文件见 [Bestiary-WithGraveyard.xlsx](Bestiary-WithGraveyard.xlsx)。保留原 8 个工作表，在敌人图鉴末尾追加 30 名敌人、62 项行动与 30 张立绘，现共 53 名敌人。新增第三行为、所属区域、占格与立绘列。原有 23 名敌人及其他工作表的内容、公式和格式保留。此操作更新本地副本，未回写在线共享原表。", "",
              "`WhisperingGraveyard.xlsx` 保留为独立增补快照，另含固定阵容试算。直接修改 Excel 不会自动回写 JSON 或 Unity。完整图鉴合并使用 `Tools/MergeGraveyardWorkbook.mjs`，由 artifact-tool 写入新内容，再由 `Tools/PreserveGraveyardWorkbook.py` 恢复原有未改动的原生文件部件，复读验证后交付。", "",
              "`python Tools/DocumentWhisperingGraveyard.py --sync-obsidian` 更新本页、同步库内的墓园说明及成品素材；新增目录为 `敌人图鉴/低语墓园` 和 `Unity资源/Enemies/WhisperingGraveyard`。", "",
              "## 验证", "",
              "Edit Mode 的 `GraveyardTests` 检查数量、占格、素材导入、每个招式、无目标回退、死亡过滤、蓄力换位、群攻、Boss 半血阶段、开场护盾和固定种子一致性。`GraveyardPlaybackTests` 验证主菜单选项、区域切换、背景和实际动作图集播放。", "",
              "`RecordReproducibleDifficultySamples` 对 11 组遭遇分别运行 100 个固定种子，用一阶未配装和二阶八件装备两套队伍，共 2200 场。测试直接读取 Unity 敌阵预设；己方站位和配装见该测试。输出 `Logs/Graveyard/Balance.csv`，本次归档见下表和 `WhisperingGraveyardBalance.csv`。测试结果仅用于比较这两套固定配置；战斗规则变更后需要重新跑样。", ""]
    results = []
    for mode in ("EditMode", "PlayMode"):
        report = ROOT / f"Logs/Graveyard/Final{mode}.xml"
        if report.exists():
            result = ET.parse(report).getroot()
            if result.attrib.get("result") == "Passed":
                results.append(f'{mode}：{result.attrib["passed"]}/{result.attrib["total"]} 通过')
    if results:
        lines += ["本次 Unity 6000.6.9f1 批处理验收：" + "；".join(results) + "。报告位于 `Logs/Graveyard/`（本地生成，不入库）。", ""]
    balance = ROOT / "Logs/Graveyard/Balance.csv"
    if balance.exists():
        records = list(csv.reader(balance.read_text(encoding="utf-8-sig").splitlines()))
        shutil.copy2(balance, ROOT / "Docs/WhisperingGraveyardBalance.csv")
        lines += ["| " + " | ".join(records[0]) + " |", "| " + " | ".join(["---"] * len(records[0])) + " |"]
        lines += ["| " + " | ".join(row) + " |" for row in records[1:]]
    return "\n".join(lines) + "\n"


def sync_vault(enemies: list[dict], text: str) -> None:
    if not VAULT.is_dir(): raise FileNotFoundError(VAULT)
    notes = VAULT / "敌人图鉴/低语墓园"
    target_art = VAULT / "Unity资源/Enemies/WhisperingGraveyard"
    notes.mkdir(parents=True, exist_ok=True)
    target_art.mkdir(parents=True, exist_ok=True)
    for source in ART.glob("*.png"):
        shutil.copy2(source, target_art / source.name)
    for source in PREVIEWS.glob("*.gif"):
        shutil.copy2(source, target_art / source.name)
    shutil.copy2(PREVIEWS / "Roster.jpg", target_art / "Roster.jpg")
    header = "<!-- Graveyard generated design, 2026-11-19 -->\n"
    index = header + "# 低语墓园敌人图鉴\n\n[[讨伐队经理/敌人图鉴/敌人图鉴|返回敌人图鉴]]\n\n23 普通、5 精英、2 Boss。完整数值与行为见本目录，动作图见 Unity资源/Enemies/WhisperingGraveyard。\n\n"
    index += "![[讨伐队经理/Unity资源/Enemies/WhisperingGraveyard/Roster.jpg|1000]]\n\n"
    index += "[[讨伐队经理/敌人图鉴/低语墓园/规则与难度|规则与难度]] · [[讨伐队经理/敌人图鉴/低语墓园/讨伐队经理-图鉴（已合并低语墓园）.xlsx|完整 Excel 图鉴]]\n\n"
    for e in enemies:
        index += f'- [[讨伐队经理/敌人图鉴/低语墓园/{e["name"]}|{e["name"]}]] · {RANK[e["rank"]]}\n'
        note = header + f'# {e["name"]}\n\n[[讨伐队经理/敌人图鉴/低语墓园/低语墓园|返回墓园图鉴]]\n\n'
        note += f'![[讨伐队经理/Unity资源/Enemies/WhisperingGraveyard/{e["id"]}-Portrait.png|256]]\n\n'
        note += f'第 {e["number"]} 号，{RANK[e["rank"]]}／{PROFESSION[e["profession"]]}，{e["slots"]} 格。\n\n'
        note += f'攻击 {e["attack"]}，防御 {e["defense"]}，特防 {e["specialDefense"]}，生命 {e["health"]}。\n\n'
        note += APPEARANCE[e["id"]] + "\n\n" + actions_table(e) + "\n\n" + e["counter"] + "\n\n"
        if e.get("openingShield"): note += f'开场护盾 {e["openingShield"]}，每场一次。\n\n'
        if e.get("phaseThreshold"): note += "半血后首次可行动时进入第二阶段，所有伤害招式攻击修正 +1，每场只进入一次。\n\n"
        note += f'![[讨伐队经理/Unity资源/Enemies/WhisperingGraveyard/{e["id"]}.gif|256]]\n'
        (notes / (e["name"] + ".md")).write_text(note, encoding="utf-8")
    (notes / "低语墓园.md").write_text(index, encoding="utf-8")
    (notes / "规则与难度.md").write_text(header + text.split("## 敌人清单")[0] + "\n完整名单见 [[讨伐队经理/敌人图鉴/低语墓园/低语墓园]]。\n", encoding="utf-8")
    balance = ROOT / "Docs/WhisperingGraveyardBalance.csv"
    if balance.exists():
        shutil.copy2(balance, notes / balance.name)
    overview = VAULT / "敌人图鉴/敌人图鉴.md"
    if overview.exists():
        original = overview.read_text(encoding="utf-8")
        old = "Excel 与 Unity 共收录 **17 名普通敌人、4 名精英及 2 名 Boss**。"
        new = "弥雾码头收录 **17 名普通敌人、4 名精英及 2 名 Boss**。低语墓园新增 **23 普通、5 精英、2 Boss**，见 [[讨伐队经理/敌人图鉴/低语墓园/低语墓园|墓园图鉴与动画]]。两区域共 53 名敌人。"
        if old in original:
            overview.write_text(original.replace(old, new, 1), encoding="utf-8")
    workbook = ROOT / "outputs/graveyard-20261119/低语墓园图鉴.xlsx"
    if workbook.exists():
        shutil.copy2(workbook, notes / workbook.name)
        shutil.copy2(workbook, ROOT / "Docs/WhisperingGraveyard.xlsx")
    merged = ROOT / "Docs/Bestiary-WithGraveyard.xlsx"
    if merged.exists():
        shutil.copy2(merged, notes / "讨伐队经理-图鉴（已合并低语墓园）.xlsx")
    print(str(notes))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sync-obsidian", action="store_true")
    parser.add_argument("--export-appearance", type=Path, metavar="JSON_PATH",
                        help="Export Chinese appearance descriptions for the workbook merge without changing documents")
    args = parser.parse_args()
    if args.export_appearance:
        args.export_appearance.parent.mkdir(parents=True, exist_ok=True)
        args.export_appearance.write_text(json.dumps(APPEARANCE, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        return
    enemies = json.loads((ROOT / "Docs/WhisperingGraveyard.json").read_text(encoding="utf-8"))["enemies"]
    text = doc(enemies)
    (ROOT / "Docs/WhisperingGraveyard.md").write_text(text, encoding="utf-8")
    if args.sync_obsidian: sync_vault(enemies, text)


if __name__ == "__main__": main()
