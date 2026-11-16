using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Battle;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.PrototypeText;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattlePrototype : MonoBehaviour
    {
        private enum Phase { Preparation, Playback, Settlement }

        [SerializeField] private EncounterAsset encounter;

        private readonly Dictionary<string, int> health = new Dictionary<string, int>();
        private readonly Dictionary<string, int> shields = new Dictionary<string, int>();
        private readonly Dictionary<string, BattleUnitCard> unitCards = new Dictionary<string, BattleUnitCard>();
        private readonly List<string> battleLog = new List<string>();

        private VisualElement root;
        private Font localFont;
        private FontAsset localFontAsset;
        private EncounterData content;
        private PartyRun run;
        private Phase phase;
        private string selectedMember;
        private int? selectedEquipmentSlot;
        private BattleRequest request;
        private BattleResult result;
        private EquipmentInstance reward;
        private int eventIndex;
        private float playbackTick;
        private float speed;
        private ScrollView logView;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            if (encounter == null)
            {
                root.Add(new Label("Assign the prototype encounter asset to BattlePrototype."));
                return;
            }

            // The prototype targets the team's Windows editor. Prefer a system CJK font when present.
            string fontName = Font.GetOSInstalledFontNames().FirstOrDefault(name => name == "Microsoft YaHei" ||
                name == "Microsoft YaHei UI" || name == "Noto Sans CJK SC" || name == "PingFang SC");
            if (fontName != null)
            {
                localFont = Font.CreateDynamicFontFromOSFont(fontName, 16);
                if (localFont != null)
                    localFontAsset = FontAsset.CreateFontAsset(localFont);
                if (localFontAsset != null)
                    root.style.unityFontDefinition = FontDefinition.FromSDFFont(localFontAsset);
            }

            content = encounter.ToData();
            ResetDemo();
        }

        private void OnDisable()
        {
            if (root != null)
            {
                root.Clear();
                root.style.unityFontDefinition = StyleKeyword.Null;
            }
            if (localFontAsset != null)
                Destroy(localFontAsset);
            if (localFont != null)
                Destroy(localFont);
        }

        private void ResetDemo()
        {
            run = new PartyRun(content.Party.Select(unit =>
                new PartyMemberStart(unit.Id, unit.Character, unit.Position, unit.Health)), content.StartingEquipment);
            phase = Phase.Preparation;
            selectedMember = run.Members[0].Id;
            selectedEquipmentSlot = null;
            request = null;
            result = null;
            reward = null;
            health.Clear();
            shields.Clear();
            battleLog.Clear();
            Render();
        }

        private void StartBattle()
        {
            if (phase != Phase.Preparation || !run.CanStartBattle)
                return;
            request = run.BeginBattle(content.Enemies, content.TickLimit, content.VictoryReward);
            result = new BattleSimulator().Simulate(request);
            foreach (var unit in request.Combatants)
                health[unit.Id] = unit.Health;

            eventIndex = 0;
            playbackTick = 0f;
            speed = 1f;
            phase = Phase.Playback;
            Render();
        }

        private void Update()
        {
            if (phase != Phase.Playback)
                return;
            playbackTick += Time.unscaledDeltaTime * 10f * speed;
            AdvancePlayback(playbackTick);
        }

        private void AdvancePlayback(float untilTick)
        {
            while (eventIndex < result.Events.Count && result.Events[eventIndex].Tick <= untilTick)
            {
                var entry = result.Events[eventIndex++];
                if (entry.Kind == BattleEventKind.Finished)
                {
                    if (!run.TrySettle(result, out reward))
                        throw new InvalidOperationException("The active replay could not be settled.");
                    phase = Phase.Settlement;
                    Render();
                    return;
                }

                if (entry.Kind == BattleEventKind.Action)
                {
                    health[entry.TargetId] = entry.TargetHealth;
                    shields[entry.TargetId] = entry.TargetShield;
                    unitCards[entry.TargetId].Refresh(entry.TargetHealth, entry.TargetShield);
                }

                var line = Describe(entry);
                battleLog.Add(line);
                AppendLog(line);
            }
        }

        private string Describe(BattleEvent entry)
        {
            string target = request.Combatants.First(unit => unit.Id == entry.TargetId).Character.Name;
            if (entry.Kind == BattleEventKind.Defeated)
                return $"[{entry.Tick:000}] {target} 倒下";
            string actor = request.Combatants.First(unit => unit.Id == entry.ActorId).Character.Name;
            string amount = entry.ActionKind == ActionKind.Heal ? $"恢复 {entry.Amount} 生命" :
                entry.ActionKind == ActionKind.Shield ? $"获得 {entry.Amount} 护盾" : $"损失 {entry.Amount} 生命";
            return $"[{entry.Tick:000}] {actor} · {entry.ActionName} → {target} · {amount}  {ReasonText(entry.Reason)}";
        }

        private void Render()
        {
            root.Clear();
            unitCards.Clear();
            root.style.backgroundColor = PrototypeStyle.Background;
            root.style.color = PrototypeStyle.Ink;

            var page = new ScrollView { name = "prototype" };
            page.style.flexGrow = 1;
            page.contentContainer.style.paddingTop = 16;
            page.contentContainer.style.paddingBottom = 16;
            page.contentContainer.style.paddingLeft = 22;
            page.contentContainer.style.paddingRight = 22;
            root.Add(page);

            var header = PrototypeStyle.Row();
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            var heading = new VisualElement();
            heading.Add(PrototypeStyle.Text("讨伐队经理", 29));
            heading.Add(PrototypeStyle.Text(content.Name, 13, PrototypeStyle.Muted));
            header.Add(heading);
            var tools = PrototypeStyle.Row();
            tools.style.alignItems = Align.Center;
            var phaseLabel = PrototypeStyle.Text(phase == Phase.Preparation ? "战前准备" :
                phase == Phase.Playback ? "自动战斗" : "战斗结算", 14, PrototypeStyle.Accent);
            phaseLabel.style.marginRight = 8;
            tools.Add(phaseLabel);
            if (phase != Phase.Playback)
                tools.Add(PrototypeStyle.Button("角色编辑", "open-party-editor", OpenPartyEditor));
            if (phase == Phase.Preparation)
            {
                var start = PrototypeStyle.Button("开始自动战斗", "start", StartBattle, true);
                start.SetEnabled(run.CanStartBattle);
                tools.Add(start);
            }
            if (phase == Phase.Playback)
                RenderPlayback(tools);
            if (phase == Phase.Settlement)
                tools.Add(PrototypeStyle.Button("重置演练", "reset", ResetDemo, true));
            header.Add(tools);
            page.Add(header);

            var arena = PrototypeStyle.Panel();
            arena.name = "battlefield";
            var opposingLines = PrototypeStyle.Row();
            opposingLines.style.alignItems = Align.Stretch;
            opposingLines.Add(BuildLine(Side.Party));
            var divider = PrototypeStyle.Text("◆", 11, PrototypeStyle.Accent);
            divider.style.width = 24;
            PrototypeStyle.Middle(divider);
            opposingLines.Add(divider);
            opposingLines.Add(BuildLine(Side.Enemy));
            arena.Add(opposingLines);
            page.Add(arena);

            RenderFooter(page);
            if (phase == Phase.Settlement)
                RenderSettlement(page);
        }

        private void OpenPartyEditor()
        {
            if (phase == Phase.Playback)
                return;

            root.Clear();
            root.Add(new PartyEditorView(run, EditorPosition, content.PortraitFor, Render));
        }

        private int? EditorPosition(MemberView member)
        {
            if (member.Position.HasValue || phase != Phase.Settlement)
                return member.Position;

            // Settlement removes defeated members from the formation. Keep their last battle slots in the editor.
            return request.Combatants.FirstOrDefault(unit => unit.Side == Side.Party && unit.Id == member.Id)?.Position;
        }

        private VisualElement BuildLine(Side side)
        {
            bool isParty = side == Side.Party;
            var line = new VisualElement();
            line.style.flexGrow = 1;
            line.style.flexBasis = 0;
            line.style.minWidth = 0;
            var title = PrototypeStyle.Text(isParty ? "己方阵列" : "敌方阵列", 13,
                isParty ? PrototypeStyle.Ally : PrototypeStyle.Enemy);
            title.style.unityTextAlign = isParty ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            title.style.marginBottom = 7;
            line.Add(title);

            var positions = PrototypeStyle.Row(isParty ? "party-positions" : "enemy-positions");
            for (int index = 0; index < Rules.Positions; index++)
                positions.Add(PositionCard(side, isParty ? Rules.Positions - index : index + 1));
            line.Add(positions);
            return line;
        }

        private VisualElement PositionCard(Side side, int position)
        {
            bool preparing = phase == Phase.Preparation;
            string id;
            CharacterDefinition character;
            int currentHealth;
            int maximumHealth;
            if (preparing && side == Side.Party)
            {
                var member = run.Members.FirstOrDefault(candidate => candidate.Position == position);
                id = member?.Id;
                character = member?.Character;
                currentHealth = member?.Health ?? 0;
                maximumHealth = member?.EffectiveStats.Health ?? 0;
            }
            else
            {
                var units = preparing ? content.Enemies : request.Combatants;
                var unit = units.FirstOrDefault(candidate => candidate.Side == side && candidate.Position == position);
                id = unit?.Id;
                character = unit?.Character;
                currentHealth = unit == null ? 0 : preparing ? unit.Health : health[id];
                maximumHealth = unit?.Stats.Health ?? 0;
            }

            bool canPlace = preparing && side == Side.Party &&
                run.Members.Any(member => member.Id == selectedMember && member.Health > 0);
            Action place = canPlace ? () =>
            {
                run.TryPlace(selectedMember, position);
                Render();
            } : null;
            string subtitle = character == null ? null : preparing && side == Side.Enemy && character.Skill != null
                ? character.Skill.Name : PersonalityText(character.Personality);
            int shield = phase == Phase.Playback && id != null ? shields.GetValueOrDefault(id) : 0;
            var card = new BattleUnitCard(side, position, id, character, content.PortraitFor(character?.Id),
                maximumHealth, currentHealth, shield, subtitle, preparing && id != null && id == selectedMember, place);
            if (id != null)
                unitCards[id] = card;
            return card;
        }

        private void RenderFooter(VisualElement page)
        {
            var bottom = PrototypeStyle.Row("battle-footer");
            bottom.style.alignItems = Align.Stretch;
            var roster = PrototypeStyle.Panel();
            roster.name = "roster-equipment";
            roster.style.flexGrow = 7;
            roster.style.flexBasis = 0;
            roster.style.minWidth = 0;
            roster.style.marginRight = 10;
            var heading = PrototypeStyle.Text("己方装备", 17);
            heading.style.marginBottom = 7;
            roster.Add(heading);

            var members = run.Members;
            var inventory = run.Inventory;
            var cards = PrototypeStyle.Row("equipment-positions");
            for (int position = Rules.Positions; position >= 1; position--)
            {
                string id = phase == Phase.Preparation
                    ? members.FirstOrDefault(member => member.Position == position)?.Id
                    : request.Combatants.FirstOrDefault(unit => unit.Side == Side.Party && unit.Position == position)?.Id;
                cards.Add(EquipmentCard(position, members.FirstOrDefault(member => member.Id == id), inventory));
            }
            roster.Add(cards);
            if (phase == Phase.Preparation)
                RenderPreparation(roster, members, inventory);
            bottom.Add(roster);
            bottom.Add(BuildLog());
            page.Add(bottom);
        }

        private VisualElement EquipmentCard(int position, MemberView member, IReadOnlyList<EquipmentInstance> inventory)
        {
            var card = PrototypeStyle.Row($"equipment-position-{position}");
            card.style.flexGrow = 1;
            card.style.flexBasis = 0;
            card.style.minWidth = 0;
            card.style.height = 113;
            card.style.marginRight = position == 1 ? 0 : 5;
            card.style.backgroundColor = PrototypeStyle.Inset;
            card.style.overflow = Overflow.Hidden;
            bool editable = phase == Phase.Preparation && member != null;
            bool selected = editable && member.Id == selectedMember;
            PrototypeStyle.Outline(card, selected ? PrototypeStyle.Accent : PrototypeStyle.Edge, 3);

            var portrait = new VisualElement();
            portrait.style.width = Length.Percent(39);
            portrait.style.overflow = Overflow.Hidden;
            var art = content.PortraitFor(member?.Character.Id);
            ProfessionFrame.AddTo(portrait, member?.Character.Profession, art);

            var caption = PrototypeStyle.Text(member?.Character.Name ?? $"{position} 号位", 10);
            caption.style.position = Position.Absolute;
            caption.style.left = caption.style.right = caption.style.bottom = 3;
            caption.style.backgroundColor = new Color(0.064f, 0.069f, 0.086f, 0.90f);
            caption.style.paddingBottom = caption.style.paddingTop = 2;
            PrototypeStyle.Middle(caption);
            portrait.Add(caption);

            if (editable)
            {
                var select = new Button(() => SelectMember(member.Id, null)) { name = $"member-{member.Id}",
                    tooltip = member.Character.Name };
                PrototypeStyle.Fill(select);
                select.style.marginTop = select.style.marginRight = select.style.marginBottom = select.style.marginLeft = 0;
                select.style.backgroundColor = Color.clear;
                PrototypeStyle.Outline(select, Color.clear, 0);
                portrait.Add(select);
            }
            card.Add(portrait);

            var slots = new VisualElement();
            slots.style.flexGrow = 1;
            slots.style.flexBasis = 0;
            slots.style.minWidth = 0;
            slots.style.justifyContent = Justify.Center;
            slots.style.paddingLeft = slots.style.paddingRight = 2;
            for (int i = 0; i < Rules.EquipmentSlots; i++)
            {
                int slot = i;
                var held = member != null ? inventory.FirstOrDefault(item => item.Id == member.EquipmentIds[slot]) : null;
                string text = $"{slot + 1} · {held?.Definition.Name ?? "空槽"}";
                if (editable)
                {
                    var button = PrototypeStyle.Button(text, selected ? $"slot-{slot}" : $"slot-{member.Id}-{slot}",
                        () => SelectMember(member.Id, slot), selected && selectedEquipmentSlot == slot);
                    button.style.fontSize = 10;
                    button.style.height = button.style.minHeight = button.style.maxHeight = 27;
                    button.style.paddingTop = button.style.paddingBottom = 0;
                    button.style.marginLeft = button.style.marginRight = 0;
                    if (held != null)
                        button.tooltip = BonusText(held.Definition.Bonus);
                    slots.Add(button);
                }
                else
                {
                    var label = PrototypeStyle.Text(text, 11, held != null ? PrototypeStyle.Ink : PrototypeStyle.Muted);
                    label.style.height = 25;
                    PrototypeStyle.Middle(label);
                    slots.Add(label);
                }
            }
            card.Add(slots);
            return card;
        }

        private void SelectMember(string id, int? slot)
        {
            selectedMember = id;
            selectedEquipmentSlot = slot;
            Render();
        }

        private void RenderPreparation(VisualElement roster, IReadOnlyList<MemberView> members,
            IReadOnlyList<EquipmentInstance> inventory)
        {
            var chosen = members.First(member => member.Id == selectedMember);
            var stats = chosen.EffectiveStats;
            var baseline = chosen.Character.BaseStats;
            var summary = PrototypeStyle.Row();
            summary.style.alignItems = Align.Center;
            summary.style.flexWrap = Wrap.Wrap;
            var statsLabel = PrototypeStyle.Text($"{chosen.Character.Name}  ·  攻 {StatText(stats.Attack, baseline.Attack)}  " +
                $"防 {StatText(stats.Defense, baseline.Defense)}  特防 {StatText(stats.SpecialDefense, baseline.SpecialDefense)}  " +
                $"生命上限 {StatText(stats.Health, baseline.Health)}", 12, PrototypeStyle.Muted);
            statsLabel.style.flexGrow = 1;
            summary.Add(statsLabel);
            var unplace = PrototypeStyle.Button("移出站位", "unplace", () =>
            {
                run.TryPlace(selectedMember, null);
                selectedEquipmentSlot = null;
                Render();
            });
            unplace.SetEnabled(chosen.Position.HasValue);
            summary.Add(unplace);
            roster.Add(summary);

            foreach (var member in members.Where(member => !member.Position.HasValue))
                roster.Add(PrototypeStyle.Button($"未布阵 · {member.Character.Name}", $"member-{member.Id}",
                    () => SelectMember(member.Id, null), member.Id == selectedMember));

            if (!selectedEquipmentSlot.HasValue)
                return;

            var tray = PrototypeStyle.Row();
            tray.style.alignItems = Align.Center;
            tray.style.flexWrap = Wrap.Wrap;
            tray.Add(PrototypeStyle.Text("共享背包", 12, PrototypeStyle.Accent));
            foreach (var item in inventory)
            {
                var holder = members.FirstOrDefault(member => member.EquipmentIds.Contains(item.Id));
                var owner = holder == null ? "背包" : holder.Character.Name;
                var button = PrototypeStyle.Button($"{item.Definition.Name} · {BonusText(item.Definition.Bonus)} · {owner}",
                    $"item-{item.Id}", () => Equip(item.Id));
                tray.Add(button);
            }

            var remove = PrototypeStyle.Button("卸下", "unequip", () => Equip(null));
            remove.SetEnabled(chosen.EquipmentIds[selectedEquipmentSlot.Value] != null);
            tray.Add(remove);
            roster.Add(tray);
        }

        private void Equip(string itemId)
        {
            if (selectedEquipmentSlot.HasValue)
                run.TryEquip(selectedMember, selectedEquipmentSlot.Value, itemId);
            Render();
        }

        private void RenderPlayback(VisualElement page)
        {
            var controls = PrototypeStyle.Row();
            var speedButton = PrototypeStyle.Button($"回放速度 ×{speed:0}", "speed", null);
            speedButton.clicked += () =>
            {
                speed = speed >= 4 ? 1 : speed * 2;
                speedButton.text = $"回放速度 ×{speed:0}";
            };
            controls.Add(speedButton);
            controls.Add(PrototypeStyle.Button("跳到结算", "skip", () => AdvancePlayback(float.PositiveInfinity)));
            page.Add(controls);
        }

        private void RenderSettlement(VisualElement page)
        {
            var summary = PrototypeStyle.Panel();
            summary.Add(PrototypeStyle.Text(OutcomeText(result.Outcome), 20, PrototypeStyle.Accent));
            summary.Add(PrototypeStyle.Text("剩余生命  " + string.Join("  ·  ", run.Members.Select(member =>
                $"{member.Character.Name} {HealthText(member.Health, member.EffectiveStats.Health)}")), 14));
            var rewardLabel = PrototypeStyle.Text(reward == null ? "本场无装备掉落" :
                $"战利品：{reward.Definition.Name}（{BonusText(reward.Definition.Bonus)}），已放入共享背包", 14);
            rewardLabel.name = "reward";
            summary.Add(rewardLabel);
            page.Add(summary);
        }

        private VisualElement BuildLog()
        {
            var panel = PrototypeStyle.Panel();
            panel.style.flexGrow = 3;
            panel.style.flexBasis = 0;
            panel.style.minWidth = 0;
            panel.Add(PrototypeStyle.Text("战斗日志", 17));
            logView = new ScrollView { name = "battle-log" };
            logView.style.height = 150;
            logView.style.marginTop = 6;
            var scroller = logView.verticalScroller;
            scroller.style.width = 11;
            scroller.lowButton.style.display = scroller.highButton.style.display = DisplayStyle.None;
            var track = scroller.slider.Q("unity-tracker");
            var thumb = scroller.slider.Q("unity-dragger");
            if (track != null)
                track.style.backgroundColor = PrototypeStyle.Inset;
            if (thumb != null)
                thumb.style.backgroundColor = PrototypeStyle.Edge;
            panel.Add(logView);
            if (battleLog.Count == 0 && phase == Phase.Preparation)
                logView.Add(PrototypeStyle.Text("战斗开始后显示行动记录。", 12, PrototypeStyle.Muted));
            foreach (string line in battleLog)
                logView.Add(PrototypeStyle.Text(line, 12));
            return panel;
        }

        private void AppendLog(string line)
        {
            var label = PrototypeStyle.Text(line, 13);
            logView.Add(label);
            label.schedule.Execute(() =>
            {
                if (label.panel != null)
                    logView.ScrollTo(label);
            });
        }

        private static string ReasonText(DecisionReason reason) => reason switch
        {
            DecisionReason.HighestDamage => "（激进：伤害优先）", DecisionReason.SelfProtection => "（慎重：自保）",
            DecisionReason.FavorAttack => "（鲁莽：先攻）", DecisionReason.WoundedAlly => "（温柔：救援）",
            DecisionReason.FollowAlly => "（配合：追击）", DecisionReason.LowestHealth => "（冷静：低血量目标）",
            _ => ""
        };

        private static string OutcomeText(BattleOutcome outcome) => outcome switch
        {
            BattleOutcome.Victory => "胜利 · 敌方全灭", BattleOutcome.PartyDefeated => "失败 · 讨伐队全灭",
            BattleOutcome.MutualDefeat => "失败 · 双方全灭", BattleOutcome.Timeout => "失败 · 达到战斗时限",
            _ => outcome.ToString()
        };
    }
}
