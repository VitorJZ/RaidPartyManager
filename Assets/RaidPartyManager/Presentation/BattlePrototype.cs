using System;
using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Battle;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.PrototypeText;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattlePrototype : MonoBehaviour
    {
        private enum Phase { Preparation, Entering, Playback, Settlement }

        [SerializeField] private EncounterAsset encounter;
        [UnityEngine.Serialization.FormerlySerializedAs("battleLogParchment")]
        [SerializeField] private Texture2D speechParchment;
        [SerializeField] private Texture2D piercingArrowParticle;
        [SerializeField] private Texture2D[] battleBackgrounds = Array.Empty<Texture2D>();
        [SerializeField] private bool randomizeLineups = true;
        [SerializeField, Min(0.05f)] private float attackAnimationTimeScale = 1f;

        private static PartyRun playModeRun;
        private static readonly Dictionary<EncounterAsset, PartyRun> enemySessions = new();
        private static readonly HashSet<EncounterAsset> editedEnemyEncounters = new();
        private readonly HashSet<string> thunderReleased = new();
        private static bool partyEdited;
        private static int playbackSpeed = 1;
        private readonly System.Random draftRandom = new System.Random();
        private bool initialized;

        private readonly Dictionary<string, int> health = new Dictionary<string, int>();
        private readonly Dictionary<string, int> shields = new Dictionary<string, int>();
        private readonly Dictionary<string, IReadOnlyList<StatusStack>> statuses = new Dictionary<string, IReadOnlyList<StatusStack>>();
        private readonly Dictionary<string, Dictionary<BattleCountdownKind, int>> countdowns = new();
        private readonly Dictionary<string, Stats> partyStats = new Dictionary<string, Stats>();
        private readonly Dictionary<string, CharacterDefinition> copiedTraits = new();
        private readonly Dictionary<string, (CharacterDefinition Character, int MaximumHealth)> appearances =
            new Dictionary<string, (CharacterDefinition, int)>();
        private readonly Dictionary<string, BattleUnitCard> unitCards = new Dictionary<string, BattleUnitCard>();
        private readonly Dictionary<string, BattleEnergy> partyEnergy = new Dictionary<string, BattleEnergy>();
        private readonly Dictionary<string, FootlightRow> energyBars = new Dictionary<string, FootlightRow>();
        // Lamps last shown per member, so a rebuilt apron can relight what the settlement refilled.
        private readonly Dictionary<string, int> litFootlights = new Dictionary<string, int>();
        private readonly Dictionary<string, BattleSpriteView> battleSprites = new Dictionary<string, BattleSpriteView>();
        private readonly BattleEntrance entrance = new BattleEntrance();
        private BattleCommentary commentary;
        private BattleTellerView tellerView;
        private readonly Dictionary<string, Side> sides = new();
        private readonly List<BattleEffectPlayback> impactEffects = new List<BattleEffectPlayback>();
        private readonly List<DockEnemyEffectPlayback> dockEffects = new List<DockEnemyEffectPlayback>();
        private readonly List<RoundEffectPlayback> roundEffects = new List<RoundEffectPlayback>();
        private readonly List<FloatingCombatNumbers> floatingNumbers = new List<FloatingCombatNumbers>();

        private VisualElement root;
        private Font localFont;
        private FontAsset localFontAsset;
        private EncounterData content;
        [SerializeField] private BattleStageArt stageArt;
        private BattleStage stage;
        private BossRoomArt bossRoom;
        private DockEventStage eventStage;
        private Action finishEventBattle;

        internal void UseEventStage(DockEventStage venue, EncounterAsset data, BattleStageArt art,
            Texture2D logPaper, Texture2D arrow, Action finish)
        {
            eventStage = venue;
            encounter = data;
            stageArt = art;
            speechParchment = logPaper;
            piercingArrowParticle = arrow;
            finishEventBattle = finish;
        }
        private VisualElement stageAnchor;
        private Texture2D battleBackground;
        private PartyRun run;
        private PartyRun enemyRun;
        private AdventureRun adventure;
        private IReadOnlyList<Combatant> preparedEnemies;
        private Phase phase;
        private BattleSession battle;
        private BattleLoadout battleLoadout;
        private IReadOnlyList<BattleEvent> roundEvents;
        private BattleResult result;
        private BattleRewards victoryRewards;
        private EquipmentDefinition[] standaloneChoices = Array.Empty<EquipmentDefinition>();
        private RunTopBar topBar;
        private bool leavingVictory;
        private bool endingTeamWipe;
        private double curtainCallUntil;
        private int eventIndex;
        private BattleActionPlayback actionPlayback;
        private BattleEvent visualAction;
        private bool waitingForImpact;
        private VisualElement actionLayer;
        private GardenBloomPlayback gardenBloom;
        private VisualElement numberLayer;
        private VisualElement meteorLayer;
        private VisualElement entranceLayer, battleFooter, formationDivider;
        private bool enemyEntranceScheduled;
        private bool settingsOpen;
        private VisualElement document;

        private double clockOrigin, playbackAtOrigin;
        private double PlaybackNow => playbackAtOrigin + (Time.unscaledTimeAsDouble - clockOrigin) * playbackSpeed;
        private BattleCharacterDetails characterDetails;
        private BattleStatusTooltip statusTooltip;
        private EquipmentTooltip equipmentTooltip;
        private VisualElement equipmentTooltipAnchor;
        private VisualElement inspectedPortrait;
        private string inspectedMember;
        private Vector2 inspectionPointer;

        // A new Play session must start fresh even when Unity skips domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaySession()
        {
            playModeRun = null;
            enemySessions.Clear();
            editedEnemyEncounters.Clear();
            partyEdited = false;
            playbackSpeed = 1;
        }

        internal static PartyRun GetPlaySession(EncounterData content)
        {
            if (playModeRun == null)
            {
                var formation = content.Draft(Side.Party, new System.Random());
                playModeRun = new PartyRun(formation.Select(unit => new PartyMemberStart(
                    unit.Id, unit.Character, unit.Position, unit.Health, unit.StartingStatuses)),
                    content.StartingEquipment.Concat(content.SkillBooks).Where(item => !item.BossExclusive && item.Effect != EquipmentEffect.StrangeSeed));
            }
            return playModeRun;
        }

        internal static void MarkPartyEdited() => partyEdited = true;

        private void OnEnable()
        {
            if (initialized)
                Initialize();
        }

        private void Start()
        {
            initialized = true;
            Initialize();
        }

        private void Initialize()
        {
            document = GetComponent<UIDocument>().rootVisualElement;
            root = new VisualElement { name = "battle-screen" };
            PrototypeStyle.Fill(root);
            document.Add(root);
            if (encounter == null)
            {
                root.Add(new Label("Assign the prototype encounter asset to BattlePrototype."));
                return;
            }

            PrototypeStyle.ApplySystemFont(document, out localFont, out localFontAsset);
            statusTooltip = new BattleStatusTooltip(root);

            content = encounter.ToData();
            adventure = AdventureFlow.ForScene(SceneManager.GetActiveScene().name);
            run = adventure?.Party ?? (playModeRun ??= new PartyRun(RosterStart(Side.Party),
                content.StartingEquipment.Concat(content.SkillBooks).Where(item => !item.BossExclusive && item.Effect != EquipmentEffect.StrangeSeed)));
            if (!enemySessions.TryGetValue(encounter, out enemyRun))
                enemySessions.Add(encounter, enemyRun = new PartyRun(RosterStart(Side.Enemy), Array.Empty<EquipmentDefinition>()));
            ResetDemo();
        }

        private IEnumerable<PartyMemberStart> RosterStart(Side side)
        {
            var formation = randomizeLineups ? content.Draft(side, draftRandom) : side == Side.Party ? content.Party : content.Enemies;
            return formation.Select(unit => new PartyMemberStart(
                unit.Id, unit.Character, unit.Position, unit.Health, unit.StartingStatuses));
        }

        private void OnDisable()
        {
            if (adventure == AdventureFlow.Active)
                adventure?.AbortBattle();
            gardenBloom = null;
            entrance.Clear(unitCards);
            ClearAction();
            ClearImpactEffects();
            statusTooltip?.Hide();
            HideEquipmentTooltip();
            PrototypeStyle.ClearDocument(document, localFont, localFontAsset);
            stage?.Dispose();
            stage = null;
        }

        private void ResetDemo(bool reroll = false)
        {
            settingsOpen = false;
            playbackAtOrigin = clockOrigin = Time.unscaledTimeAsDouble;
            entrance.Clear(unitCards);
            ClearAction();
            ClearImpactEffects();
            if (adventure != null)
                run.CancelBattle();
            else if (reroll && randomizeLineups && !partyEdited)
                run.ResetRoster(RosterStart(Side.Party));
            else
                run.ResetEncounter();
            if (reroll && randomizeLineups && !editedEnemyEncounters.Contains(encounter))
                enemyRun.ResetRoster(RosterStart(Side.Enemy));
            standaloneChoices = Array.Empty<EquipmentDefinition>();
            victoryRewards = adventure?.Current?.Battle?.Reward;
            preparedEnemies = victoryRewards != null ? Array.Empty<Combatant>() :
                adventure != null ? adventure.PrepareEnemies(content.EnemyRoster, content.EnemyFormations) : enemyRun.CaptureFormation(Side.Enemy);
            phase = Phase.Preparation;
            battle = null;
            battleLoadout = null;
            roundEvents = null;
            result = null;
            leavingVictory = false;
            payrollPromptShown = false;
            endingTeamWipe = false;
            curtainCallUntil = 0;
            health.Clear();
            shields.Clear();
            statuses.Clear();
            countdowns.Clear();
            partyStats.Clear();
            partyEnergy.Clear();
            appearances.Clear();
            copiedTraits.Clear();
            battleSprites.Clear();
            commentary = new BattleCommentary();
            sides.Clear();
            var backgrounds = battleBackgrounds.Where(texture => texture != null).ToArray();
            battleBackground = backgrounds.Length > 0 ? backgrounds[draftRandom.Next(backgrounds.Length)] : null;
            if (gameObject.scene.name == "ClockworkCapital" && backgrounds.Length == 3)
            {
                var boss = preparedEnemies.FirstOrDefault(e => e.Character.Rank == EnemyRank.Boss);
                int district = boss?.Character.Id switch
                {
                    "EnemyRoyalEngine" => 0, "EnemyCrucibleRegent" => 1, "EnemyAstralGovernor" => 2,
                    _ => adventure == null ? draftRandom.Next(3) : Mathf.Clamp((adventure.Stage - 1) / 3, 0, 2)
                };
                battleBackground = backgrounds[district];
            }
            else if (adventure != null)
                battleBackground = backgrounds.FirstOrDefault(texture =>
                    texture.name.StartsWith(adventure.InCapital ? "07-" : adventure.InGraveyard ? "04-" : "01-")) ?? battleBackground;
            DressBattleStage(true);
            if (victoryRewards != null)
            {
                phase = Phase.Settlement;
                Render();
                return;
            }
            if (eventStage == null) entrance.Add(run.CaptureFormation(Side.Party), content, stage);
            enemyEntranceScheduled = false;
            RenderPreparation();
        }

        private void DressBattleStage(bool reset = false)
        {
            var enemies = victoryRewards != null ? adventure?.Current?.Battle?.Enemies : preparedEnemies;
            var boss = enemies?.FirstOrDefault(unit => unit.Character.Rank == EnemyRank.Boss);
            var room = boss != null && eventStage == null
                ? Resources.Load<BossRoomArt>("BossRooms/" + boss.Character.Id) : null;
            if (!reset && room == bossRoom) return;
            bossRoom = room;
            tellerView = null;
            stage?.Dispose();
            stage = null;
            battleSprites.Clear();
            var scenery = bossRoom != null ? bossRoom.Dress() : stageArt?.For(battleBackground);
            if (eventStage != null)
                stage = eventStage.BattleCast(root, stageArt, PlaybackNow);
            else if (scenery != null && stageArt != null && Camera.main is { } sceneCamera)
                stage = new BattleStage(transform, sceneCamera, root, stageArt, scenery, PlaybackNow);
        }

        private bool payrollPromptShown;
        private bool FailedBeforeFinalBoss => battle == null && adventure is { Failed: true } && adventure.PayrollDepartures.Count > 0;
        private bool AutoStarts => eventStage != null || adventure is { IsInspection: false, NeedsFinalPayroll: false };

        private void ShowFinalPayroll()
        {
            if (root.Q("payroll-panel") != null) return;
            payrollPromptShown = true;
            VisualElement sheet = null;
            sheet = ContractPanel.RepayWages(run, () => sheet.RemoveFromHierarchy(), () => topBar.Refresh(), () =>
            {
                sheet.RemoveFromHierarchy();
                if (adventure.ConfirmFinalPayroll())
                {
                    if (adventure.PayrollDepartures.Count > 0)
                        ClearEntranceForEditing();
                    AdventureFlow.AutoSave(adventure);
                    StartBattle();
                }
                else
                {
                    if (FailedBeforeFinalBoss)
                    {
                        ClearEntranceForEditing();
                        AdventureFlow.EndWipedRoute();
                    }
                    Render();
                }
            });
            root.Add(sheet);
            sheet.Q<Button>("payroll-pay")?.Focus();
        }

        private void RenderPreparation()
        {
            if (phase == Phase.Preparation && AutoStarts && run.CanStartBattle)
            {
                StartBattle();
                return;
            }
            if (phase == Phase.Preparation && !enemyEntranceScheduled && CanPreviewEnemies())
            {
                entrance.Add(preparedEnemies, content, stage);
                enemyEntranceScheduled = true;
            }
            Render();
            if (!payrollPromptShown && adventure?.NeedsFinalPayroll == true)
                ShowFinalPayroll();
        }

        private void StartBattle()
        {
            if (phase != Phase.Preparation || !run.CanStartBattle)
                return;
            if (adventure?.NeedsFinalPayroll == true)
            {
                ShowFinalPayroll();
                return;
            }
            bool bossBattle = preparedEnemies.Any(enemy => enemy.Character.Rank == EnemyRank.Boss);
            var request = run.BeginBattle(preparedEnemies, content.TickLimit, adventure != null || bossBattle ? null : content.VictoryReward,
                content.RecruitmentPool, content.CharacterCatalog, adventure?.Current?.Kind is NodeKind.EliteBattle or NodeKind.Boss,
                adventure?.InCapital == true || gameObject.scene.name == "ClockworkCapital");
            adventure?.BindBattle(request.Id);
            GameAudio.Play(SoundCue.UiBattleStart);
            battle = new BattleSimulator().Start(request);
            commentary.Opening(preparedEnemies.FirstOrDefault(e => e.Character.Rank == EnemyRank.Boss)?.Character.Name,
                Time.unscaledTimeAsDouble);
            ReadPartyStats();
            ReadPartyEnergy();
            foreach (var unit in battle.Formation)
            {
                sides[unit.Id] = unit.Side;
                health[unit.Id] = unit.Health;
                appearances[unit.Id] = (unit.Character, unit.Stats.Health);
                if (unit.StartingStatuses.Count > 0)
                    statuses[unit.Id] = unit.StartingStatuses.OrderBy(status => status.Key)
                        .Select(status => new StatusStack(status.Key, status.Value)).ToArray();
            }

            if (!enemyEntranceScheduled)
            {
                entrance.Add(battle.Formation.Where(unit => unit.Side == Side.Enemy), content, stage);
                enemyEntranceScheduled = true;
            }

            if (entrance.Active)
            {
                phase = Phase.Entering;
                Render();
            }
            else
                BeginNextRound();
        }

        private void BeginNextRound()
        {
            if (battle.Round > 0) GameAudio.Play(SoundCue.UiTurnAdvance, .39f);
            roundEvents = battle.AdvanceRound();
            thunderReleased.Clear();
            if (battle.Round == 1 && battle.OpeningLoadout != null)
                ApplyLoadout(battle.OpeningLoadout);
            foreach (var unit in battle.Formation)
            {
                sides[unit.Id] = unit.Side;
                health.TryAdd(unit.Id, unit.Health);
                if (unit.Side == Side.Party)
                    partyEnergy.TryAdd(unit.Id, BattleEnergy.Full(unit.Equipment, unit.BonusEnergy));
                appearances.TryAdd(unit.Id, (unit.Character, unit.Stats.Health));
            }
            eventIndex = 0;
            phase = Phase.Playback;
            Render();
        }

        private void ReadPartyEnergy()
        {
            foreach (var entry in battle.ReadPartyEnergy())
                partyEnergy[entry.Key] = entry.Value;
        }

        private void ReadPartyStats()
        {
            foreach (var entry in battle.ReadPartyStats())
                partyStats[entry.Key] = entry.Value;
        }

        private void Update()
        {
            // Entrance steps advance only when presented. Start the action clock after the curtain opens.
            if (DockPlayhouse.Moving)
                return;

            double now = PlaybackNow;
            if (gardenBloom != null && !gardenBloom.Draw(root, now))
            {
                gardenBloom = null;
                Render();
            }
            if (entrance.Active)
                entrance.Show(now, entranceLayer, unitCards, playbackSpeed);
            if (battleFooter != null && !Mathf.Approximately(battleFooter.style.opacity.value, entrance.HudOpacity))
            {
                battleFooter.style.opacity = entrance.HudOpacity;
                formationDivider.style.opacity = entrance.HudOpacity;
            }
            if (phase == Phase.Entering && !entrance.Active)
                BeginNextRound();
            else if (phase == Phase.Playback)
                AdvancePlayback(now);
            foreach (var sprite in battleSprites.Values)
                if (sprite.panel != null)
                    sprite.UpdateFrame(now);
            if (actionPlayback != null)
                actionPlayback.Draw(now, actionLayer, meteorLayer, unitCards.GetValueOrDefault(visualAction.ActorId),
                    unitCards);
            for (int i = impactEffects.Count - 1; i >= 0; i--)
            {
                var effect = impactEffects[i];
                if (effect.ActiveAt(now))
                    effect.Draw(actionLayer, unitCards.GetValueOrDefault(effect.TargetId), now);
                else
                {
                    effect.Clear();
                    impactEffects.RemoveAt(i);
                }
            }
            for (int i = dockEffects.Count - 1; i >= 0; i--)
            {
                var effect = dockEffects[i];
                if (effect.ActiveAt(now))
                    effect.Draw(actionLayer, unitCards.GetValueOrDefault(effect.ActorId),
                        unitCards.GetValueOrDefault(effect.TargetId), now);
                else
                {
                    effect.Clear();
                    dockEffects.RemoveAt(i);
                }
            }
            for (int i = roundEffects.Count - 1; i >= 0; i--)
            {
                var effect = roundEffects[i];
                if (effect.ActiveAt(now))
                    effect.Draw(actionLayer, effect.ActorId != null ? unitCards.GetValueOrDefault(effect.ActorId) : null,
                        unitCards.GetValueOrDefault(effect.TargetId), now);
                else
                {
                    effect.Clear();
                    roundEffects.RemoveAt(i);
                }
            }
            for (int i = floatingNumbers.Count - 1; i >= 0; i--)
            {
                var numbers = floatingNumbers[i];
                if (numbers.ActiveAt(now))
                    numbers.Draw(numberLayer, unitCards.GetValueOrDefault(numbers.TargetId)?.BodyBounds ?? default, now);
                else
                {
                    numbers.RemoveFromHierarchy();
                    floatingNumbers.RemoveAt(i);
                }
            }
            if (endingTeamWipe && floatingNumbers.Count == 0 && Time.unscaledTimeAsDouble >= curtainCallUntil)
            {
                endingTeamWipe = false;
                RenderDefeat();
            }
        }

        private void LateUpdate()
        {
            if (root == null || root.layout.height <= 0) return;
            // Keep the boards level with the feet on every formation and viewport.
            var foot = stageAnchor?.worldBound ?? default;
            float baseline = foot.height > 0 ? 1 - (foot.yMax - 3 - root.worldBound.yMin) / root.layout.height : .3f;
            float focus = actionPlayback != null && visualAction.ActorId != null && unitCards.TryGetValue(visualAction.ActorId, out var actor)
                ? (actor.BodyBounds.center.x - root.worldBound.center.x) / (root.layout.width * .5f) : 0;
            stage?.Update(PlaybackNow, baseline, focus, Time.unscaledDeltaTime * playbackSpeed);
            if (tellerView?.panel == null || DockPlayhouse.Moving) return;
            double now = Time.unscaledTimeAsDouble;
            commentary.Advance(now);
            float totalHeight = 0;
            int count = 0;
            foreach (var card in unitCards.Values)
            {
                float h = card.PartyBodyHeight;
                if (!(h > 0)) continue;
                totalHeight += h;
                count++;
            }
            if (count > 0) tellerView.Draw(commentary, now, foot, totalHeight / count, unitCards.Values);
        }

        private void AdvancePlayback(double now)
        {
            actionPlayback?.SoundAt(now);
            if (actionPlayback != null && !waitingForImpact && now >= actionPlayback.FinishAt)
                ClearAction();

            while (eventIndex < roundEvents.Count)
            {
                var entry = roundEvents[eventIndex];
                if (entry.Kind == BattleEventKind.RoundStarted)
                {
                    if (HasActiveAnimation(now))
                        return;
                    eventIndex++;
                    continue;
                }
                if (entry.Countdown is { } timer)
                {
                    // Protective badges come off only after the blow they absorbed has finished playing,
                    // otherwise the bell or stone guard vanishes just as the enemy swing lands.
                    if (timer.Remaining == 0 && timer.Kind is BattleCountdownKind.GoldenBell or BattleCountdownKind.StoneGuard &&
                        HasActiveAnimation(now))
                        return;
                    Narrate(entry);
                    UpdateCountdown(entry.TargetId, timer, now);
                    eventIndex++;
                    continue;
                }

                bool isRoundEndEffect = RoundEffectPlayback.IsRoundEnd(entry.VisualCue);
                bool isRoundStartEffect = RoundEffectPlayback.IsRoundStart(entry.VisualCue);
                bool showRoundEndEffect = isRoundEndEffect && entry.Detail != "状态未生效" && entry.Detail != "未命中";
                // Status-only changes can settle while an earlier round-end effect is still fading out.
                bool statusChanged = ChangesStatusWithoutAnimation(entry);
                bool waitingForRoundEndEffect = showRoundEndEffect
                    ? actionPlayback != null || roundEffects.Any(effect => !effect.AtRoundStart &&
                        effect.TargetId == entry.TargetId && effect.HoldsTargetAt(now))
                    : !isRoundEndEffect && !statusChanged && entry.Kind != BattleEventKind.Defeated &&
                        roundEffects.Any(effect => !effect.AtRoundStart && effect.ActiveAt(now));
                if (waitingForRoundEndEffect)
                    return;

                if (entry.Kind == BattleEventKind.EquipmentVisual)
                {
                    GameAudio.Play(BattleSounds.Equipment(entry.Equipment), .48f);
                    QueueEquipmentEffect(entry, now);
                    eventIndex++;
                    continue;
                }

                // A fog flash belongs to its target and has no impact or character pose of its own.
                if (entry.Kind == BattleEventKind.EnemyVisual)
                {
                    dockEffects.Add(new DockEnemyEffectPlayback(entry, now, AnimationScale));
                    eventIndex++;
                    continue;
                }

                bool piercingInFlight = waitingForImpact && actionPlayback?.IsPiercing == true;
                if (piercingInFlight && entry.VisualCue == BattleVisualCue.PiercingArrow && entry.ActorId == visualAction.ActorId)
                {
                    if (now < actionPlayback.NextPiercingImpactAt)
                        return;
                    waitingForImpact = actionPlayback.AdvancePiercingHit();
                }
                else if (entry.Kind == BattleEventKind.Action || entry.Kind == BattleEventKind.Effect &&
                    entry.VisualCue == BattleVisualCue.RaySurge && !piercingInFlight ||
                    entry.SkillBookId == null && entry.AttackReach != AttackReach.Unspecified)
                {
                    if (waitingForImpact)
                    {
                        if (now < actionPlayback.ImpactAt)
                            return;
                        actionPlayback.HandleImpact();
                        waitingForImpact = false;
                        if (now >= actionPlayback.FinishAt)
                            ClearAction();
                    }
                    else
                    {
                        if (HasActiveAnimation(now, allowSignatureOverlap: true))
                            return;
                        if (BeginAction(entry, now))
                            return;
                    }
                }
                if (entry.Kind == BattleEventKind.Finished)
                {
                    SpeakResult();
                    if (Time.unscaledTimeAsDouble < curtainCallUntil || HasActiveAnimation(now) ||
                        !TeamWiped && floatingNumbers.Any(numbers => numbers.ActiveAt(now))) return;
                }
                eventIndex++;

                if (entry.Kind == BattleEventKind.SkillVisual)
                {
                    QueueSkillEffect(entry, now);
                    continue;
                }

                if (entry.Kind == BattleEventKind.Finished)
                {
                    SyncCompletedRoundHealth();
                    if (TeamWiped)
                    {
                        FinishTeamWipe();
                        return;
                    }

                    SettleBattle();
                    foreach (var member in run.Members)
                    {
                        if (member.Health <= 0 || !result.HealthById.TryGetValue(member.Id, out int finalHealth) || finalHealth != 0)
                            continue;
                        health[member.Id] = member.Health;
                        shields.Remove(member.Id);
                        statuses.Remove(member.Id);
                    }
                    ReadPartyEnergy();
                    phase = Phase.Settlement;
                    if (result.Outcome != BattleOutcome.Victory)
                        Render();
                    else if (adventure?.Current?.Battle?.BloomWearer is { } wearer &&
                        unitCards.GetValueOrDefault(wearer) is { } card)
                    {
                        gardenBloom = new GardenBloomPlayback(stage, content.IconFor("strange-seed"),
                            content.BattleWalkFor(AdventureRun.GraveGardenerId), root.WorldToLocal(card.BodyBounds), now);
                    }
                    else
                        Render();
                    return;
                }

                if (entry.TargetId != null && appearances.TryGetValue(entry.TargetId, out var subject))
                {
                    if (entry.Kind == BattleEventKind.Effect && entry.SkillBookId == "skill-book-30" &&
                        thunderReleased.Add(entry.ActorId)) GameAudio.Play(SoundCue.SkillThunderVerdictRelease);
                    if (entry.Kind == BattleEventKind.Defeated && !battleSprites.ContainsKey(entry.TargetId))
                        GameAudio.Play(BattleSounds.Fall(subject.Character));
                    bool arrow = entry.AttackReach == AttackReach.Ranged && appearances.TryGetValue(entry.ActorId ?? "", out var attacker)
                        && (attacker.Character.Profession == Profession.Archer || attacker.Character.Id == "EnemyForkFisher" ||
                            attacker.Character.Trait == CharacterTrait.EnemyDeckSailor);
                    BattleSounds.Impact(entry, subject.Character, arrow, statuses.GetValueOrDefault(entry.TargetId),
                        health.GetValueOrDefault(entry.TargetId, entry.TargetHealth));
                }
                if (entry.CopiedTraitSource != null) copiedTraits[entry.TargetId] = entry.CopiedTraitSource;
                Narrate(entry);
                if (entry.ActorEnergy is { } energy)
                {
                    partyEnergy[entry.ActorId] = energy;
                    PaintEnergy(entry.ActorId, energy);
                }
                if (entry.TargetEnergy is { } targetEnergy)
                {
                    partyEnergy[entry.TargetId] = targetEnergy;
                    PaintEnergy(entry.TargetId, targetEnergy);
                }
                if (entry.PartyStatChanges != null)
                    foreach (var change in entry.PartyStatChanges)
                        partyStats[change.Key] = change.Value;
                if (entry.Loadout != null)
                    ApplyLoadout(entry.Loadout);
                if (entry.Kind != BattleEventKind.Defeated)
                {
                    int absorbed = Math.Max(0, shields.GetValueOrDefault(entry.TargetId) - entry.TargetShield);
                    if (entry.Kind == BattleEventKind.Retreated && battleSprites.TryGetValue(entry.TargetId, out var departing))
                        departing.PlayRetreat(now, content.BattleWalkFor(appearances[entry.TargetId].Character.Id));
                    if (entry.ActionKind == ActionKind.Attack &&
                        (entry.Amount > 0 || absorbed > 0) &&
                        battleSprites.TryGetValue(entry.TargetId, out var targetSprite) && targetSprite.panel != null)
                        targetSprite.PlayHit(now);

                    int healed = entry.ActionKind == ActionKind.Heal ? entry.Amount :
                        entry.TargetHealth - health.GetValueOrDefault(entry.TargetId, entry.TargetHealth);
                    if (healed > 0 || entry.DamageTaken.Count > 0)
                    {
                        int lane = floatingNumbers.Count(numbers => numbers.TargetId == entry.TargetId && numbers.RecentlyStarted(now));
                        Rect body = unitCards.GetValueOrDefault(entry.TargetId)?.BodyBounds ?? default;
                        floatingNumbers.Add(new FloatingCombatNumbers(entry, healed, body, now, lane));
                    }
                    health[entry.TargetId] = entry.TargetHealth;
                    shields[entry.TargetId] = entry.TargetShield;
                    statuses[entry.TargetId] = entry.StatusStacks;
                    if (entry.MaximumHealth > 0)
                        appearances[entry.TargetId] = (entry.Form ?? appearances[entry.TargetId].Character, entry.MaximumHealth);
                    if (entry.Form != null)
                    {
                        Render();
                    }
                    else if (entry.Loadout != null)
                        Render();
                    else
                        unitCards[entry.TargetId].Refresh(entry.TargetHealth, entry.TargetShield, now, entry.StatusStacks,
                            countdowns.GetValueOrDefault(entry.TargetId), entry.MaximumHealth);
                    bool equipmentEffectShown = QueueEquipmentEffect(entry, now);
                    if (entry.Detail != "未命中")
                    {
                        if (showRoundEndEffect || isRoundStartEffect && entry.Detail != "状态未生效")
                        {
                            int lane = isRoundStartEffect
                                ? roundEffects.Count(effect => effect.AtRoundStart && effect.TargetId == entry.TargetId && effect.ActiveAt(now))
                                : 0;
                            roundEffects.Add(new RoundEffectPlayback(entry, absorbed, now, AnimationScale,
                                entry.VisualCue == BattleVisualCue.RoundEndTransfer ? content.IconFor("megaphone") : null, lane,
                                drawParticles: !equipmentEffectShown));
                        }
                        else if (DockEnemyEffectPlayback.Supports(entry.VisualCue))
                            dockEffects.Add(new DockEnemyEffectPlayback(entry, now, AnimationScale));
                        else if (!isRoundEndEffect && !isRoundStartEffect)
                        {
                            var effect = EffectArt(entry);
                            // An unassigned Unity texture can still satisfy a C# type pattern.
                            if (effect != null)
                                impactEffects.Add(new BattleEffectPlayback(entry.TargetId, effect, now, AnimationScale));
                            else if (!equipmentEffectShown || entry.Kind == BattleEventKind.Action)
                            {
                                var signature = SignatureFor(entry);
                                if (signature != null)
                                {
                                    float sourceX = unitCards.GetValueOrDefault(entry.ActorId)?.BodyBounds.center.x ?? 0;
                                    float targetX = unitCards.GetValueOrDefault(entry.TargetId)?.BodyBounds.center.x ?? 0;
                                    impactEffects.Add(new BattleEffectPlayback(entry.TargetId, signature, sourceX > targetX, now, AnimationScale));
                                }
                            }
                        }
                    }
                    if (entry.CharmTriggered)
                    {
                        var charm = Resources.Load<Texture2D>("EquipmentEffects/ElvenCharm");
                        if (charm != null)
                            impactEffects.Add(new BattleEffectPlayback(entry.TargetId, charm, now, AnimationScale, charm: true));
                    }
                    QueueSkillEffect(entry, now);
                }

                if (entry.TargetId == inspectedMember || entry.PartyStatChanges != null)
                    RefreshCharacterDetails();

                if (entry.TargetHealth == 0 && TeamWiped && battle.Formation
                    .Where(unit => unit.Side == Side.Party).All(unit => health[unit.Id] == 0))
                {
                    FinishTeamWipe();
                    return;
                }
            }

            if (HasActiveAnimation(now))
                return;

            SyncCompletedRoundHealth();
            ReadPartyStats();
            BeginNextRound();
        }

        // AdvanceRound simulates ahead of the actors on stage. Only reconcile after its final visible hit.
        private void SyncCompletedRoundHealth()
        {
            foreach (var (id, current, maximum) in battle.ReadHealth())
            {
                if (health[id] == current && appearances[id].MaximumHealth == maximum) continue;
                health[id] = current;
                appearances[id] = (appearances[id].Character, maximum);
                unitCards[id].Refresh(current, shields.GetValueOrDefault(id), PlaybackNow, statuses.GetValueOrDefault(id),
                    countdowns.GetValueOrDefault(id), maximum);
            }
        }

        private void UpdateCountdown(string id, BattleCountdown timer, double now)
        {
            if (!countdowns.TryGetValue(id, out var timers))
                countdowns.Add(id, timers = new Dictionary<BattleCountdownKind, int>());
            if (timer.Remaining == 0)
                timers.Remove(timer.Kind);
            else
                timers[timer.Kind] = timer.Remaining;

            if (unitCards.TryGetValue(id, out var card))
                card.Refresh(health.GetValueOrDefault(id), shields.GetValueOrDefault(id), now, statuses.GetValueOrDefault(id), timers);
            if (inspectedMember == id)
                RefreshCharacterDetails();
        }

        private bool ChangesStatusWithoutAnimation(BattleEvent entry)
        {
            if (entry.Kind != BattleEventKind.Effect || entry.ActionKind != ActionKind.Shield ||
                entry.VisualCue != BattleVisualCue.None || entry.Form != null || entry.Loadout != null)
                return false;

            var previous = statuses.GetValueOrDefault(entry.TargetId) ?? Array.Empty<StatusStack>();
            var current = entry.StatusStacks ?? Array.Empty<StatusStack>();
            return entry.TargetShield != shields.GetValueOrDefault(entry.TargetId) || !previous.SequenceEqual(current);
        }

        private bool TeamWiped => battle.Result?.Outcome is BattleOutcome.PartyDefeated or BattleOutcome.MutualDefeat;

        private void SpeakResult()
        {
            if (curtainCallUntil > 0) return;
            double now = Time.unscaledTimeAsDouble;
            commentary.Result(battle.Result.Outcome, now);
            curtainCallUntil = now + 1.85;
        }

        private void FinishTeamWipe()
        {
            SpeakResult();
            SettleBattle();
            phase = Phase.Settlement;
            if (adventure is { IsInspection: false })
                AdventureFlow.EndWipedRoute();
            topBar.SetEnabled(false);
            endingTeamWipe = true;
        }

        private void SettleBattle()
        {
            if (TeamWiped)
                GameAudio.Play(SoundCue.ResultPartyWipe);
            else if (battle.Result.Outcome == BattleOutcome.Victory)
                GameAudio.Play(preparedEnemies.Any(e => e.Character.Rank == EnemyRank.Boss)
                    ? SoundCue.ResultBossVictory : SoundCue.ResultVictory);
            result = battle.Result;
            if (result.Outcome != BattleOutcome.Victory)
            {
                if (!run.TrySettle(result, out _) || adventure != null && !adventure.SettleBattle(result))
                    throw new InvalidOperationException("The battle loss could not be settled.");
                return;
            }

            if (!run.TryFinishBattle(result, out var loot))
                throw new InvalidOperationException("The active replay could not be settled.");
            int gold;
            if (adventure == null)
                gold = AdventureRun.RollKillGold(result, battle.Formation.Where(unit => unit.Side == Side.Enemy), draftRandom);
            else if (!adventure.SettleBattle(result, out gold))
                throw new InvalidOperationException("The route encounter could not be settled.");

            int totalGold = (int)Math.Min(int.MaxValue - run.Gold, (long)result.GoldEarned + gold);
            if (adventure == null && preparedEnemies.FirstOrDefault(e => e.Character.Rank == EnemyRank.Boss) is { } boss)
            {
                var spoils = BossSpoils.Roll(boss.Character.Id, content.StartingEquipment.Concat(content.SkillBooks), draftRandom);
                standaloneChoices = spoils.Choices;
                victoryRewards = new BattleRewards(totalGold, loot.Append(spoils.Equipment));
            }
            else victoryRewards = adventure != null ? adventure.RecordBattleRewards(totalGold, loot) : new BattleRewards(totalGold, loot);
        }

        private bool HasActiveAnimation(double now, bool allowSignatureOverlap = false) => actionPlayback != null ||
            impactEffects.Any(effect => (!allowSignatureOverlap || effect.BlocksNextAction) && effect.ActiveAt(now)) ||
            dockEffects.Any(effect => effect.ActiveAt(now)) ||
            roundEffects.Any(effect => !effect.AtRoundStart && effect.ActiveAt(now)) ||
            battleSprites.Values.Any(sprite => sprite.panel != null && sprite.IsReactingAt(now));

        private float AnimationScale => Mathf.Max(0.05f, attackAnimationTimeScale);

        private Texture2D EffectArt(BattleEvent entry) =>
            entry.VisualCue != BattleVisualCue.None && entry.ActorId != null && appearances.TryGetValue(entry.ActorId, out var actor)
                ? content.BattleEffectFor(actor.Character.Id, entry.VisualCue) : null;

        private Texture2D SignatureFor(BattleEvent entry) =>
            entry.SkillBookId == null && entry.AttackReach != AttackReach.Unspecified &&
            entry.ActionKind == ActionKind.Attack && entry.ActorId != null && entry.ActorId != entry.TargetId &&
            appearances.TryGetValue(entry.ActorId, out var actor)
                ? Resources.Load<Texture2D>("AttackEffects/" + actor.Character.Id) : null;

        private bool QueueEquipmentEffect(BattleEvent entry, double now)
        {
            if (entry.Equipment == EquipmentEffect.None) return false;
            var atlas = EquipmentEffectArt.For(entry.Equipment);
            if (atlas == null) return false;

            string target = entry.Equipment == EquipmentEffect.WindGlove ? entry.ActorId : entry.TargetId;
            if (target == null) return false;
            // Several doses or stacked copies can resolve together. Keep one readable cue per item and unit.
            if (impactEffects.Any(effect => effect.Equipment == entry.Equipment &&
                effect.TargetId == target && effect.ActiveAt(now))) return true;
            int lane = impactEffects.Count(effect => effect.Equipment != EquipmentEffect.None &&
                effect.TargetId == target && effect.ActiveAt(now));
            var icon = content.StartingEquipment.FirstOrDefault(item => item.Effect == entry.Equipment);
            impactEffects.Add(new BattleEffectPlayback(target, atlas, now, AnimationScale,
                equipment: entry.Equipment, icon: icon == null || entry.VisualCue == BattleVisualCue.RoundEndTransfer
                    ? null : content.IconFor(icon.Id), equipmentLane: lane));
            return true;
        }

        private void QueueSkillEffect(BattleEvent entry, double now)
        {
            if (entry.SkillBookId == null || entry.TargetId == null || entry.ActorEnergy.HasValue || entry.Detail == "未命中")
                return;
            var atlas = content.SkillEffectFor(entry.SkillBookId, entry.VisualCue);
            if (atlas == null)
                return;
            if (impactEffects.Any(effect => effect.SkillBookId == entry.SkillBookId &&
                effect.TargetId == entry.TargetId && effect.ActiveAt(now)))
                return;
            // The cloud gathers over the caster. The bolt hits the enemies on the following round.
            if (entry.SkillBookId == "skill-book-30" && entry.Kind == BattleEventKind.SkillVisual)
                atlas = Resources.Load<Texture2D>("ThunderGathering");
            impactEffects.Add(new BattleEffectPlayback(entry.TargetId, atlas, now, AnimationScale, entry.SkillBookId));
        }

        private bool BeginAction(BattleEvent entry, double now)
        {
            if (entry.ActorId == null || !appearances.TryGetValue(entry.ActorId, out var appearance))
                return false;

            var character = appearance.Character;
            var sprite = battleSprites.GetValueOrDefault(entry.ActorId);
            if (sprite?.panel == null)
                sprite = null;
            Texture2D walking = null;
            Texture2D projectile = null;
            Texture2D fragments = null;
            IReadOnlyList<BattleEvent> projectileHits = null;
            Texture2D signatureProjectile = null;
            bool isArrow = character.Profession == Profession.Archer || character.Id is "EnemyForkFisher" or "DeepSeaWhisper" ||
                character.Trait == CharacterTrait.EnemyDeckSailor;
            bool isSweep = entry.Kind == BattleEventKind.Action && entry.SkillBookId != null && entry.ActionKind == ActionKind.Attack;
            if (entry.ActionKind == ActionKind.Attack && entry.ActorId != entry.TargetId)
            {
                if (isSweep)
                    (projectile, fragments) = content.SkillProjectileFor(entry.SkillBookId);
                if (projectile != null)
                {
                    isArrow = fragments == null;
                    projectileHits = roundEvents.Skip(eventIndex)
                        .TakeWhile((hit, offset) => offset == 0 || hit.Kind != BattleEventKind.Action)
                        .Where(hit => hit.ActorId == entry.ActorId && hit.SkillBookId == entry.SkillBookId &&
                            hit.ActionKind == ActionKind.Attack)
                        .ToArray();
                }
                else if (entry.AttackReach == AttackReach.Melee)
                    walking = content.BattleWalkFor(character.Id) ?? (sprite?.UsesPortrait == true ? sprite.Sheet : null);
                else if (!isSweep)
                {
                    string projectileId = entry.VisualCue == BattleVisualCue.PiercingArrow &&
                        copiedTraits.TryGetValue(entry.ActorId, out var source) ? source.Id : character.Id;
                    projectile = content.AuthoredProjectileFor(projectileId);
                    if (projectile == null && !DockEnemyEffectPlayback.Supports(entry.VisualCue) && EffectArt(entry) == null)
                        signatureProjectile = SignatureFor(entry);
                    if (signatureProjectile == null)
                        projectile ??= content.ProjectileFor(character);

                    if (projectile != null && entry.VisualCue is BattleVisualCue.AdmiralBroadside or BattleVisualCue.PiercingArrow)
                        projectileHits = roundEvents.Skip(eventIndex)
                            .TakeWhile((hit, offset) => offset == 0 || hit.Kind != BattleEventKind.Action)
                            .Where(hit => hit.ActorId == entry.ActorId && hit.VisualCue == entry.VisualCue)
                            .ToArray();
                }
            }
            bool hasProjectile = projectile != null || signatureProjectile != null;
            if (sprite == null && !hasProjectile)
                return false;

            var book = entry.ActorEnergy.HasValue ? content.SkillBooks.FirstOrDefault(skill => skill.Id == entry.SkillBookId) : null;
            var pose = book?.SkillEffect switch
            {
                SkillEffect.WarCry or SkillEffect.SweepNextAttack or SkillEffect.StoneGuard or SkillEffect.GoldenBell => ActionKind.Shield,
                not null => ActionKind.Heal,
                _ => entry.ActionKind
            };
            actionPlayback = new BattleActionPlayback(sprite, pose, walking, projectile, fragments, isArrow, entry, projectileHits,
                now, AnimationScale, content.BattleSpecialFor(character.Id, entry.VisualCue),
                DockEnemyEffectPlayback.Supports(entry.VisualCue) || EffectArt(entry) != null ||
                entry.SkillBookId != null && book?.SkillEffect != SkillEffect.SweepNextAttack,
                entry.VisualCue == BattleVisualCue.CrossbowCritical, cannon: character.Trait == CharacterTrait.EnemyBlockadeAdmiral,
                piercingParticle: piercingArrowParticle,
                releaseSound: BattleSounds.Release(entry, character, hasProjectile, isArrow),
                signatureProjectile: signatureProjectile);
            if (walking != null) GameAudio.Play(BattleSounds.Step(character), .34f);
            visualAction = entry;
            waitingForImpact = true;
            return true;
        }

        private void ClearAction()
        {
            actionPlayback?.Clear();
            actionPlayback = null;
            waitingForImpact = false;
        }

        private void ClearImpactEffects()
        {
            foreach (var effect in impactEffects)
                effect.Clear();
            impactEffects.Clear();
            foreach (var effect in dockEffects)
                effect.Clear();
            dockEffects.Clear();
            foreach (var effect in roundEffects)
                effect.Clear();
            roundEffects.Clear();
            foreach (var numbers in floatingNumbers)
                numbers.RemoveFromHierarchy();
            floatingNumbers.Clear();
        }

        private void Narrate(BattleEvent entry)
        {
            if (entry.TargetId == null || !appearances.TryGetValue(entry.TargetId, out var target)) return;
            string actor = entry.ActorId != null && appearances.TryGetValue(entry.ActorId, out var source) ? source.Character.Name : null;
            commentary.Observe(entry, actor, target.Character.Name, sides[entry.TargetId] == Side.Party,
                health.GetValueOrDefault(entry.TargetId), shields.GetValueOrDefault(entry.TargetId),
                statuses.GetValueOrDefault(entry.TargetId), Time.unscaledTimeAsDouble,
                entry.Kind == BattleEventKind.Action && entry.SkillBookId != null
                    ? content.SkillBooks.FirstOrDefault(book => book.Id == entry.SkillBookId)?.SkillEffect ?? SkillEffect.None
                    : SkillEffect.None);
        }

        private void ApplyLoadout(BattleLoadout loadout)
        {
            if (!run.TryApplyBattleLoadout(battle.Id, loadout))
                throw new InvalidOperationException("Battle equipment no longer matches the party inventory.");
            battleLoadout = loadout;
        }

        private void Render()
        {
            Vector2? previousInspection = inspectedPortrait != null ? inspectionPointer : null;
            HideCharacterDetails();
            statusTooltip.Hide();
            HideEquipmentTooltip();
            root.Clear();
            unitCards.Clear();
            energyBars.Clear();
            root.style.backgroundColor = stage == null ? PrototypeStyle.Background : Color.clear;
            root.style.color = PrototypeStyle.Ink;

            if (stage == null)
            {
                var backdrop = new VisualElement { name = "battle-background", pickingMode = PickingMode.Ignore };
                PrototypeStyle.Fill(backdrop);
                if (battleBackground != null)
                {
                    SceneryFlipbook.Animate(backdrop, battleBackground);
                    backdrop.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                    backdrop.style.unityBackgroundImageTintColor = new Color(0.78f, 0.78f, 0.78f, 1f);
                }
                root.Add(backdrop);
            }

            tellerView ??= new BattleTellerView(stage, speechParchment);
            root.Add(tellerView);

            bool defeat = phase == Phase.Settlement && victoryRewards == null;
            bool setup = !FailedBeforeFinalBoss && (phase == Phase.Preparation || defeat);
            topBar = new RunTopBar(root, run, content, setup ? OpenPartyEditor : null, actions: BattleControls(defeat),
                settingsChanged: SetSettingsOpen, modalRoot: document,
                openEnemyEditor: setup && content.EnemyRoster.Count > 0 && adventure == null ? OpenEnemyEditor : null);
            root.Add(topBar);
            var page = new ScrollView { name = "prototype" };
            page.style.flexGrow = 1;
            page.style.minHeight = 0;
            page.verticalScroller.valueChanged += _ => HideEquipmentTooltip();
            page.contentContainer.style.flexGrow = 1;
            page.contentContainer.style.paddingTop = 8;
            page.contentContainer.style.paddingLeft = page.contentContainer.style.paddingRight =
                stage == null ? new Length(22) : Length.Percent(stage.HasDockHouse ? 7.5f : 3.5f);
            root.Add(page);
            if (adventure?.PayrollDepartures.Count > 0)
                page.Add(PrototypeStyle.Text("因欠薪离队 · " + string.Join("、", adventure.PayrollDepartures), 16, PrototypeStyle.Accent));
            if (FailedBeforeFinalBoss)
                page.Add(PrototypeStyle.Text(adventure.Current.Message, 18, PrototypeStyle.Accent));

            var arena = new VisualElement { name = "battlefield" };
            arena.style.marginTop = StyleKeyword.Auto;
            arena.style.flexShrink = 0;
            bool showEnemies = battle != null || phase == Phase.Preparation && CanPreviewEnemies();
            int largestEnemy = showEnemies ? (battle?.Formation ?? preparedEnemies)
                .Where(unit => unit.Side == Side.Enemy).Select(unit => unit.Character.PositionSlots).DefaultIfEmpty(1).Max() : 1;
            float portraitHeight = PortraitHeight(largestEnemy);
            var opposingLines = PrototypeStyle.Row();
            opposingLines.style.alignItems = Align.Stretch;
            var partyLine = BuildLine(Side.Party, portraitHeight);
            opposingLines.Add(partyLine);
            var divider = PrototypeStyle.Text("◆", 11, PrototypeStyle.Accent);
            divider.style.width = 24;
            divider.visible = showEnemies;
            PrototypeStyle.Middle(divider);
            formationDivider = divider;
            opposingLines.Add(divider);
            opposingLines.Add(BuildLine(Side.Enemy, portraitHeight, showEnemies));
            arena.Add(opposingLines);
            var apron = BuildApron(previousInspection, partyLine);
            battleFooter = apron;
            apron.style.opacity = divider.style.opacity = entrance.HudOpacity;
            arena.Add(apron);
            stageAnchor = opposingLines.Q("combatant-portrait");
            actionLayer = new VisualElement { name = "battle-action-layer", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(actionLayer);
            arena.Add(actionLayer);
            entranceLayer = new VisualElement { name = "battle-entrance-layer", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(entranceLayer);
            entranceLayer.style.overflow = Overflow.Hidden;
            arena.Add(entranceLayer);
            page.Add(arena);
            if (entrance.Active)
                entrance.Show(PlaybackNow, entranceLayer, unitCards, playbackSpeed);

            // A falling meteor can extend above the arena without changing the scrollable area.
            meteorLayer = new VisualElement { name = "battle-meteor-layer", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(meteorLayer);
            meteorLayer.style.top = RunTopBar.Height;
            meteorLayer.style.overflow = Overflow.Hidden;
            root.Add(meteorLayer);

            if (stage != null)
            {
                var clearance = new VisualElement { name = "audience-clearance", pickingMode = PickingMode.Ignore };
                clearance.style.height = Length.Percent(AudienceForeground.ClearancePercent);
                clearance.style.flexShrink = 0;
                root.Add(clearance);
            }
            numberLayer = new VisualElement { name = "battle-number-layer", pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(numberLayer);
            numberLayer.style.top = RunTopBar.Height;
            root.Add(numberLayer);
            if (defeat)
            {
                page.SetEnabled(false);
                if (TeamWiped) topBar.SetEnabled(false);
                if (!endingTeamWipe) RenderDefeat();
            }
            else if (phase == Phase.Settlement)
            {
                page.SetEnabled(false);
                if (gardenBloom == null) RenderVictory();
            }
        }

        private VisualElement BattleControls(bool defeat)
        {
            var controls = PrototypeStyle.Row("battle-controls");
            controls.style.alignItems = Align.Center;
            controls.style.flexShrink = 0;
            controls.style.marginLeft = 7;
            if (phase == Phase.Preparation)
            {
                if (FailedBeforeFinalBoss)
                    controls.Add(PrototypeStyle.Button("返回主菜单", "payroll-return",
                        () => PrototypeSceneMenu.OpenScene("MainMenu"), true));
                else if (adventure?.NeedsFinalPayroll == true)
                    controls.Add(PrototypeStyle.Button("战前结薪", "start", ShowFinalPayroll, true));
                else if (AutoStarts)
                    controls.Add(PrototypeStyle.Text("请先为在场队员安排站位", 13, PrototypeStyle.Accent));
                else
                {
                    var start = PrototypeStyle.Button("开始战斗", "start", StartBattle, true);
                    start.SetEnabled(run.CanStartBattle);
                    controls.Add(start);
                }
                return controls;
            }

            var label = PrototypeStyle.Text(phase switch
            {
                Phase.Entering => "入场",
                Phase.Playback => $"第 {battle.Round} 回合",
                _ => "战斗结算"
            }, 13, PrototypeStyle.Accent);
            label.name = "battle-phase";
            label.style.marginRight = 6;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            controls.Add(label);
            if (phase is Phase.Entering or Phase.Playback)
            {
                var speed = PrototypeStyle.Button($"倍速 {playbackSpeed}×", "battle-speed", null);
                speed.clicked += () =>
                {
                    playbackAtOrigin = PlaybackNow;
                    clockOrigin = Time.unscaledTimeAsDouble;
                    playbackSpeed = playbackSpeed == 1 ? 2 : 1;
                    speed.text = $"倍速 {playbackSpeed}×";
                };
                speed.tooltip = "切换 1 倍或 2 倍战斗播放速度";
                controls.Add(speed);
            }
            if (defeat)
            {
                if (adventure == null)
                    controls.Add(PrototypeStyle.Button("重置演练", "reset", () => ResetDemo(reroll: true), true));
                else
                    controls.Add(PrototypeStyle.Button("返回事件选择", "return-event-select", AdventureFlow.ReturnFromNode));
            }
            return controls;
        }

        private void SetSettingsOpen(bool open)
        {
            settingsOpen = open;
            if (open)
            {
                HideCharacterDetails();
                statusTooltip.Hide();
                HideEquipmentTooltip();
            }
            else if (phase == Phase.Settlement && victoryRewards != null && gardenBloom == null &&
                !victoryRewards.HasUnclaimed && LootChoices.Count == 0 && root.Q("continue-after-ratings") == null)
                ContinueVictory();
        }

        private void OpenPartyEditor()
        {
            if (phase != Phase.Preparation && phase != Phase.Settlement)
                return;

            ClearEntranceForEditing();
            statusTooltip.Hide();
            HideEquipmentTooltip();
            root.Q<RunTopBar>().EditParty(RenderPreparation, () => partyEdited = true);
        }

        private void OpenEnemyEditor()
        {
            if (phase != Phase.Preparation && phase != Phase.Settlement)
                return;

            ClearEntranceForEditing();
            statusTooltip.Hide();
            HideEquipmentTooltip();
            root.Q<RunTopBar>().EditParty(() =>
            {
                preparedEnemies = enemyRun.CaptureFormation(Side.Enemy);
                DressBattleStage();
                RenderPreparation();
            }, () =>
            {
                editedEnemyEncounters.Add(encounter);
                enemyEntranceScheduled = false;
            }, enemyRun);
        }

        private void ClearEntranceForEditing()
        {
            if (entrance.EnemyEntrancePending)
                enemyEntranceScheduled = false;
            entrance.Clear(unitCards);
        }

        private bool CanPreviewEnemies() =>
            run.GraveScout || run.Members.Any(member => member.Health > 0 && member.Character.Trait == CharacterTrait.RevealEnemies);

        private static float PortraitHeight(int slots) => 174 + (slots - 1) * 28;

        private VisualElement BuildLine(Side side, float portraitHeight, bool showUnits = true)
        {
            bool isParty = side == Side.Party;
            var line = new VisualElement();
            line.style.flexGrow = 1;
            line.style.flexBasis = 0;
            line.style.minWidth = 0;
            if (!showUnits)
                return line;

            var positions = PrototypeStyle.Row(isParty ? "party-positions" : "enemy-positions");
            for (int index = 0; index < Rules.Positions; index++)
            {
                int slot = isParty ? Rules.Positions - index : index + 1;
                var card = PositionCard(side, slot, portraitHeight);
                int width = isParty ? 1 : card?.PositionSlots ?? 1;
                var slotView = (VisualElement)card ?? new VisualElement { pickingMode = PickingMode.Ignore };
                slotView.name = $"{(isParty ? "party" : "enemy")}-position-{slot}";
                slotView.style.flexGrow = width;
                slotView.style.flexBasis = 0;
                slotView.style.minWidth = 0;
                slotView.style.marginLeft = slotView.style.marginRight = 4;
                positions.Add(slotView);
                index += width - 1;
            }
            line.Add(positions);
            return line;
        }

        private BattleUnitCard PositionCard(Side side, int position, float portraitHeight)
        {
            bool preparing = battle == null;
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
                var units = preparing ? preparedEnemies : battle.Formation;
                var unit = units.FirstOrDefault(candidate => candidate.Side == side && candidate.Position == position);
                id = unit?.Id;
                var appearance = id != null && !preparing ? appearances[id] : (unit?.Character, unit?.Stats.Health ?? 0);
                character = appearance.Item1;
                currentHealth = unit == null ? 0 : preparing ? unit.Health : health[id];
                maximumHealth = appearance.Item2;
            }

            if (id == null)
                return null;

            int shield = !preparing && phase != Phase.Settlement ? shields.GetValueOrDefault(id) : 0;
            BattleSpriteView sprite = null;
            var sheet = content.BattleSpriteFor(character?.Id);
            var illustration = sheet ?? content.BattlePortraitFor(character?.Id);
            if (illustration != null)
            {
                if (!battleSprites.TryGetValue(id, out sprite) || sprite.Sheet != illustration)
                    battleSprites[id] = sprite = new BattleSpriteView(illustration, content.BattleAttackFor(character.Id),
                        character.Profession, position, side == Side.Enemy, stage, usesPortrait: sheet == null, fallSound: BattleSounds.Fall(character));
                sprite.Figure.Performer = new DockSet.Player(side == Side.Enemy, content.BattleWalkFor(character.Id), sheet);
                sprite.style.top = 5 + portraitHeight - PortraitHeight(character.PositionSlots);
                sprite.RemoveFromHierarchy();
            }
            var card = new BattleUnitCard(side, id, character, content.PortraitFor(character?.Id),
                maximumHealth, currentHealth, shield, PlaybackNow, portraitHeight,
                statusIcon: content.StatusIcon, battleSprite: sprite, statusTooltip: statusTooltip);
            card.Refresh(currentHealth, shield, PlaybackNow, statuses.GetValueOrDefault(id),
                phase == Phase.Settlement ? null : countdowns.GetValueOrDefault(id));
            unitCards[id] = card;
            return card;
        }

        // The party's loadout lives on the stage lip under each actor: props on a shelf, energy as footlights.
        private VisualElement BuildApron(Vector2? previousInspection, VisualElement partyLine)
        {
            var apron = PrototypeStyle.Row("battle-apron");
            apron.style.alignItems = Align.FlexEnd;
            apron.style.flexShrink = 0;
            apron.style.marginTop = 10;
            // Room for the flickering lamp halos: anything poking below the page makes it scroll.
            apron.style.marginBottom = 14;

            var members = run.Members;
            var inventory = run.Inventory;
            var cards = PrototypeStyle.Row("equipment-positions");
            cards.style.flexGrow = 1;
            cards.style.flexBasis = 0;
            cards.style.minWidth = 0;
            cards.style.alignItems = Align.FlexEnd;
            for (int position = Rules.Positions; position >= 1; position--)
            {
                string id = battle == null
                    ? members.FirstOrDefault(member => member.Position == position)?.Id
                    : battle.Formation.FirstOrDefault(unit => unit.Side == Side.Party && unit.Position == position)?.Id;
                var member = members.FirstOrDefault(entry => entry.Id == id);
                var character = phase != Phase.Preparation && id != null && appearances.TryGetValue(id, out var appearance)
                    ? appearance.Character : member?.Character;
                var equipment = id != null && battleLoadout != null && battleLoadout.Slots.TryGetValue(id, out var slots)
                    ? slots : member?.EquipmentIds;
                var skills = member == null ? Array.Empty<SkillBookDefinition>() :
                    member.LearnedSkills.Take(member.SkillSlotCount).ToArray();
                cards.Add(EquipmentCard(position, id, character, equipment, inventory, skills, previousInspection, member?.BonusEnergy ?? 0));
            }
            apron.Add(cards);
            // Match the party line exactly so every lamp run stays under its actor whatever the divider measures.
            partyLine.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!(partyLine.layout.width > 0)) return;
                cards.style.flexGrow = 0;
                cards.style.flexShrink = 0;
                cards.style.flexBasis = partyLine.layout.width;
            });
            var aisle = new VisualElement { pickingMode = PickingMode.Ignore };
            aisle.style.width = 24;
            apron.Add(aisle);
            var enemyLip = new VisualElement { pickingMode = PickingMode.Ignore };
            enemyLip.style.flexGrow = 1;
            enemyLip.style.flexBasis = 0;
            apron.Add(enemyLip);

            if (phase == Phase.Preparation)
            {
                var notes = new VisualElement { name = "apron-notes", pickingMode = PickingMode.Ignore };
                notes.style.position = Position.Absolute;
                notes.style.left = 4;
                notes.style.bottom = 2;
                foreach (var member in members.Where(member => !member.Position.HasValue))
                    notes.Add(ApronNote($"未布阵 · {member.Character.Name}"));
                if (run.HasRecruitmentVacancy && members.Count == Rules.Positions)
                    notes.Add(ApronNote("寻人启事成功召来援军后消耗。存活的援军会占用一个队伍名额。"));
                if (notes.childCount > 0)
                    apron.Add(notes);
            }
            return apron;

            static Label ApronNote(string text)
            {
                var note = PrototypeStyle.Text(text, 12, PrototypeStyle.Muted);
                note.style.textShadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 3, color = Color.black };
                return note;
            }
        }

        private VisualElement EquipmentCard(int position, string id, CharacterDefinition character,
            IReadOnlyList<string> equipment, IReadOnlyList<EquipmentInstance> inventory,
            IReadOnlyList<SkillBookDefinition> skills, Vector2? previousInspection, int bonusEnergy)
        {
            var card = new VisualElement { name = $"equipment-position-{position}" };
            card.style.flexGrow = 1;
            card.style.flexBasis = 0;
            card.style.minWidth = 0;
            card.style.marginLeft = card.style.marginRight = 4;
            card.style.alignItems = Align.Center;
            card.style.justifyContent = Justify.FlexEnd;
            if (id == null)
            {
                card.pickingMode = PickingMode.Ignore;
                return card;
            }
            card.pickingMode = PickingMode.Ignore;

            // Footlight wash: a violet beam rising toward the actor and a pool lighting the props from below.
            var beam = new VisualElement { name = "footlight-beam", pickingMode = PickingMode.Ignore };
            beam.style.position = Position.Absolute;
            beam.style.left = beam.style.right = Length.Percent(-4);
            beam.style.bottom = FootlightRow.LampHeight * .4f;
            beam.style.height = 150;
            beam.style.backgroundImage = ApronArt.Beam;
            beam.style.unityBackgroundImageTintColor = (Color)new Color32(176, 104, 255, 255);
            card.Add(beam);
            var pool = ApronArt.Soft("footlight-pool", new Color32(205, 128, 255, 255));
            pool.style.left = pool.style.right = Length.Percent(-6);
            pool.style.bottom = FootlightRow.LampHeight * .5f;
            pool.style.height = 64;
            card.Add(pool);

            // Props stand on a low oak riser just upstage of the lamps; the riser is drawn first so props sit on its top.
            var stand = new VisualElement { name = "equipment-stand", pickingMode = PickingMode.Ignore };
            stand.style.maxWidth = Length.Percent(100);
            stand.style.paddingLeft = stand.style.paddingRight = 4;
            var shelf = new VisualElement { name = "equipment-shelf", pickingMode = PickingMode.Ignore };
            shelf.style.position = Position.Absolute;
            shelf.style.left = shelf.style.right = shelf.style.bottom = 0;
            shelf.style.height = RiserHeight;
            if (ApronArt.Riser != null)
            {
                shelf.style.backgroundImage = ApronArt.Riser;
                shelf.style.unitySliceLeft = shelf.style.unitySliceRight = RiserSlice;
                shelf.style.unitySliceScale = RiserHeight / ApronArt.Riser.height;
                // The painted riser carries a warm highlight; cool it toward the violet footlights.
                shelf.style.unityBackgroundImageTintColor = new Color(.86f, .8f, .9f);
            }
            else
            {
                shelf.style.backgroundColor = (Color)new Color32(74, 49, 33, 255);
                PrototypeStyle.Outline(shelf, new Color32(150, 112, 62, 255), 3);
            }
            var riserShadow = ApronArt.Soft("equipment-shelf-shadow", new Color(0, 0, 0, .6f));
            riserShadow.style.left = riserShadow.style.right = Length.Percent(-3);
            riserShadow.style.bottom = -5;
            riserShadow.style.height = 12;
            stand.Add(riserShadow);
            stand.Add(shelf);
            var slots = PrototypeStyle.Row("equipment-slots");
            slots.pickingMode = PickingMode.Ignore;
            slots.style.justifyContent = Justify.Center;
            slots.style.alignItems = Align.FlexEnd;
            slots.style.paddingLeft = slots.style.paddingRight = 6;
            slots.style.marginBottom = RiserHeight - RiserTopDepth;
            var equippedItems = new EquipmentDefinition[equipment?.Count ?? Rules.EquipmentSlots];
            var disabledSlots = new bool[equippedItems.Length];
            var tiles = new List<VisualElement>();
            for (int slot = 0; slot < equippedItems.Length; slot++)
            {
                var held = inventory.FirstOrDefault(item => item.Id == equipment?[slot])?.Definition;
                equippedItems[slot] = held;
                bool disabled = phase == Phase.Playback && held != null && battleLoadout?.Disabled.Contains(equipment[slot]) == true;
                disabledSlots[slot] = disabled;
                var tile = EquipmentTile($"equipment-slot-{slot + 1}", held, slot + 1, disabled);
                tiles.Add(tile);
                slots.Add(tile);
            }
            stand.Add(slots);
            card.Add(stand);
            card.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!(card.layout.width > 0)) return;
                float size = Mathf.Clamp(Mathf.Floor((card.layout.width - 20) / tiles.Count) - 4, 18, 38);
                foreach (var tile in tiles)
                    tile.style.width = tile.style.height = size;
            });

            var energy = phase != Phase.Preparation && partyEnergy.TryGetValue(id, out var current)
                ? current : BattleEnergy.Full(equippedItems.Where(item => item != null), bonusEnergy);
            var lamps = new FootlightRow(energy, litFootlights.TryGetValue(id, out int lit) ? lit : -1);
            // The lamps stand on the lip in front of the riser, overlapping its foot for depth.
            lamps.style.marginTop = -8;
            lamps.Illuminate(beam, pool);
            // Footlights sit below and in front, so each prop throws a long shadow upstage that breathes with the flames
            // and fades out entirely once every lamp in the run is dark.
            var casts = card.Query("equipment-cast-shadow").ToList();
            if (casts.Count > 0)
                card.schedule.Execute(() =>
                {
                    float reach = 1.25f + .22f * lamps.Flicker;
                    foreach (var cast in casts)
                    {
                        cast.style.opacity = .55f * lamps.Brightness;
                        cast.style.scale = new Scale(new Vector2(1.05f + .04f * lamps.Flicker, reach));
                    }
                }).Every(16);
            litFootlights[id] = energy.Remaining;
            energyBars[id] = lamps;
            card.Add(lamps);

            // The actor on stage stands in for the old roster portrait.
            if (unitCards.TryGetValue(id, out var actor))
            {
                var portrait = actor.AddInspectionTarget($"equipment-portrait-{position}");
                if (phase == Phase.Playback)
                    RegisterCharacterInspection(portrait, position, id, character, equippedItems, disabledSlots, skills, previousInspection);
            }
            return card;
        }

        private void PaintEnergy(string id, BattleEnergy energy)
        {
            if (id == null) return;
            if (energyBars.TryGetValue(id, out var lamps))
                lamps.Paint(energy);
            litFootlights[id] = energy.Remaining;
        }

        // Measured on PropRiser.png: how tall the riser draws, how deep its top surface reads, and its end-cap width.
        private const float RiserHeight = 30, RiserTopDepth = 9;
        private const int RiserSlice = 34;

        private VisualElement EquipmentTile(string name, EquipmentDefinition item, int? slot = null, bool disabled = false)
        {
            var view = new VisualElement { name = name };
            view.style.width = view.style.height = 30;
            view.style.minWidth = 0;
            view.style.marginLeft = view.style.marginRight = 3;
            view.style.flexShrink = 0;
            if (item == null)
            {
                // An empty slot is a chalked mark waiting for its prop.
                view.pickingMode = PickingMode.Ignore;
                if (ApronArt.Chalk != null)
                {
                    // A chalk ring drawn flat on the riser top, foreshortened, marking where a prop would stand.
                    var mark = new VisualElement { name = "equipment-empty-mark", pickingMode = PickingMode.Ignore };
                    mark.style.position = Position.Absolute;
                    mark.style.left = mark.style.right = Length.Percent(8);
                    mark.style.bottom = -RiserTopDepth * .55f;
                    mark.style.height = Length.Percent(34);
                    mark.style.backgroundImage = ApronArt.Chalk;
                    mark.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                    mark.style.unityBackgroundImageTintColor = new Color(.92f, .86f, .78f, .38f);
                    view.Add(mark);
                }
                else
                {
                    var empty = PrototypeStyle.Text(slot?.ToString() ?? "", 11, PrototypeStyle.Muted);
                    empty.pickingMode = PickingMode.Ignore;
                    empty.style.flexGrow = 1;
                    PrototypeStyle.Middle(empty);
                    view.Add(empty);
                }
                return view;
            }

            var artwork = content.IconFor(item.Id);
            if (artwork != null)
            {
                var cast = new Image { name = "equipment-cast-shadow", image = artwork, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                cast.tintColor = new Color(.05f, .02f, .08f, 1);
                cast.style.position = Position.Absolute;
                cast.style.left = cast.style.right = cast.style.top = cast.style.bottom = 0;
                cast.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(92));
                cast.style.opacity = 0;
                var blur = new FilterFunction(FilterFunctionType.Blur);
                blur.AddParameter(new FilterParameter(1.6f));
                cast.style.filter = new List<FilterFunction> { blur };
                view.Add(cast);
            }
            var contact = ApronArt.Soft("equipment-shadow", new Color(0, 0, 0, .7f));
            contact.style.left = contact.style.right = Length.Percent(14);
            contact.style.bottom = -3;
            contact.style.height = Length.Percent(22);
            view.Add(contact);
            view.Add(new EquipmentIcon(item, artwork, disabled: disabled));
            if (disabled && ApronArt.Seal != null)
            {
                var cloth = new VisualElement { name = "equipment-sealed", pickingMode = PickingMode.Ignore };
                cloth.style.position = Position.Absolute;
                // The drape covers the lower part so the sealed prop still peeks out above the chain.
                cloth.style.left = cloth.style.right = Length.Percent(-8);
                cloth.style.top = Length.Percent(30);
                cloth.style.bottom = Length.Percent(-12);
                cloth.style.backgroundImage = ApronArt.Seal;
                cloth.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                cloth.style.opacity = .88f;
                view.Add(cloth);
            }
            RegisterEquipmentTooltip(view, item, artwork, disabled);
            return view;
        }

        private void RegisterEquipmentTooltip(VisualElement view, EquipmentDefinition item, Texture2D artwork, bool disabled = false)
        {
            view.RegisterCallback<PointerEnterEvent>(evt =>
            {
                if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse)
                    return;
                HideEquipmentTooltip();
                equipmentTooltipAnchor = view;
                equipmentTooltip = new EquipmentTooltip(item, artwork, disabled: disabled);
                equipmentTooltip.ShowAbove(root, view);
            });
            view.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (equipmentTooltipAnchor == view)
                    HideEquipmentTooltip();
            });
        }

        private void HideEquipmentTooltip()
        {
            equipmentTooltip?.RemoveFromHierarchy();
            equipmentTooltip = null;
            equipmentTooltipAnchor = null;
        }

        private void RegisterCharacterInspection(VisualElement portrait, int position, string id, CharacterDefinition character,
            IReadOnlyList<EquipmentDefinition> equipment, IReadOnlyList<bool> disabledSlots, IReadOnlyList<SkillBookDefinition> skills, Vector2? previousInspection)
        {
            portrait.RegisterCallback<PointerEnterEvent>(evt =>
            {
                if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse)
                    Show((Vector2)evt.position);
            });
            portrait.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (inspectedPortrait == portrait)
                    inspectionPointer = (Vector2)evt.position;
            });
            portrait.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (inspectedPortrait == portrait)
                    HideCharacterDetails();
            });
            if (previousInspection.HasValue)
                portrait.RegisterCallback<GeometryChangedEvent>(RestoreInspection);

            void RestoreInspection(GeometryChangedEvent evt)
            {
                portrait.UnregisterCallback<GeometryChangedEvent>(RestoreInspection);
                if (inspectedPortrait != null || !portrait.worldBound.Contains(previousInspection.Value))
                    return;
                var target = root.panel?.Pick(previousInspection.Value);
                if (target != null && (target == portrait || portrait.Contains(target)))
                    Show(previousInspection.Value);
            }

            void Show(Vector2 pointer)
            {
                HideCharacterDetails();
                inspectedPortrait = portrait;
                inspectedMember = id;
                inspectionPointer = pointer;
                var member = run.Members.FirstOrDefault(m => m.Id == id);
                var details = new BattleCharacterDetails(position, character, equipment, skills, content.IconFor,
                    member?.Trait ?? PlayerTrait.None, member?.Personality ?? character.Personality, disabledSlots);
                characterDetails = details;
                details.RegisterCallback<GeometryChangedEvent>(_ => details.PositionAt(root, portrait));
                root.Add(details);
                RefreshCharacterDetails();
                details.PositionAt(root, portrait);
            }
        }

        private void RefreshCharacterDetails()
        {
            if (characterDetails != null && inspectedMember != null && copiedTraits.TryGetValue(inspectedMember, out var source))
                characterDetails.ShowCopiedTrait(source);
            if (characterDetails == null || !partyStats.TryGetValue(inspectedMember, out var stats))
                return;
            characterDetails.Refresh(stats, health[inspectedMember], shields.GetValueOrDefault(inspectedMember),
                statuses.GetValueOrDefault(inspectedMember), phase == Phase.Settlement ? null : countdowns.GetValueOrDefault(inspectedMember));
        }

        private void HideCharacterDetails()
        {
            characterDetails?.RemoveFromHierarchy();
            characterDetails = null;
            inspectedPortrait = null;
            inspectedMember = null;
        }

        private IReadOnlyList<EquipmentDefinition> LootChoices =>
            adventure?.Current is { Resolved: false } visit ? visit.Rewards : standaloneChoices;

        private void ContinueVictory()
        {
            if (gardenBloom != null) return;
            var overlay = root.Q("victory-rewards");
            if (victoryRewards.HasUnclaimed || LootChoices.Count > 0)
            {
                if (overlay == null)
                    Render();
                else
                    RenderVictory();
                return;
            }

            AdventureFlow.AutoSaveResolvedVisit(adventure);
            HideEquipmentTooltip();
            overlay?.RemoveFromHierarchy();
            if (topBar.HasRewardFlights)
            {
                topBar.WhenRewardsArrive(ContinueVictory);
                return;
            }
            if (leavingVictory || settingsOpen)
                return;

            leavingVictory = true;
            if (finishEventBattle != null) finishEventBattle();
            else if (adventure == null) PrototypeSceneMenu.OpenStandalone("MainMenu");
            else AdventureFlow.ReturnFromNode();
        }

        private VisualElement SettlementPanel(string overlayId, string panelId, string title)
        {
            HideEquipmentTooltip();
            root.Q(overlayId)?.RemoveFromHierarchy();
            var overlay = new VisualElement { name = overlayId };
            PrototypeStyle.Fill(overlay);
            overlay.style.top = RunTopBar.Height;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.backgroundColor = new Color(0.03f, 0.026f, 0.033f, 0.76f);
            var panel = PrototypeStyle.Panel();
            panel.name = panelId;
            panel.style.width = Length.Percent(94);
            panel.style.maxWidth = 840;
            panel.style.maxHeight = Length.Percent(94);
            panel.style.paddingTop = panel.style.paddingBottom = 10;
            panel.style.paddingLeft = panel.style.paddingRight = 14;
            panel.style.marginTop = 0;
            PrototypeStyle.Outline(panel, PrototypeStyle.Accent, 6);
            var heading = PrototypeStyle.Text(title, 24, PrototypeStyle.Accent);
            PrototypeStyle.Middle(heading);
            panel.Add(heading);
            overlay.Add(panel);
            root.Add(overlay);
            return panel;
        }

        private void AddBattleRatings(VisualElement parent)
        {
            if (adventure?.Current is { Kind: NodeKind.Boss, Battle: { Settled: true } })
                parent.Add(new FloorRatingChart(adventure));
            parent.Add(new BattleScoreboard(result?.Ratings ?? adventure?.Current?.Battle?.Ratings, content));
        }

        private void RenderVictory()
        {
            var panel = SettlementPanel("victory-rewards", "victory-panel", "胜利");
            var rewards = new ScrollView(ScrollViewMode.Vertical);
            rewards.style.flexShrink = 1;
            panel.Add(rewards);
            AddBattleRatings(rewards);
            if (adventure?.Current?.Battle?.Kind == NodeKind.Boss)
                rewards.Add(PrototypeStyle.Text("首领已击败，全队恢复 50% 最大生命值。", 15, PrototypeStyle.Ink));

            if (adventure?.Current?.Battle?.BloomWaitingForRoom == true)
                rewards.Add(PrototypeStyle.Text("种子已经成熟。腾出一个队伍空位，再装备它赢一场即可招募花匠。", 15, PrototypeStyle.Ink));

            var row = RewardRow();
            for (int i = 0; i < victoryRewards.Items.Count; i++)
            {
                int index = i;
                if (victoryRewards.Items[index] == null)
                    continue;
                ItemButton(row, victoryRewards.Items[index], "claim-loot-" + index,
                    () => victoryRewards.ClaimItem(run, index));
            }
            if (victoryRewards.Gold > 0)
                row.Add(RewardButton($"{victoryRewards.Gold} 金币", "claim-gold", new GoldRewardIcon(),
                    button => Claim(button, () => victoryRewards.ClaimGold(run))));
            var choices = LootChoices;
            var choiceRow = choices.Count > 1 ? RewardRow() : row;
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                ItemButton(choiceRow, choices[index], "claim-route-loot-" + index,
                    () => {
                        if (adventure != null) return adventure.ClaimReward(index);
                        if (index >= standaloneChoices.Length) return false;
                        run.AddEquipment(standaloneChoices[index]);
                        standaloneChoices = Array.Empty<EquipmentDefinition>();
                        return true;
                    });
            }
            if (row.childCount > 0)
            {
                var lootHeading = PrototypeStyle.Text("战利品", 16, PrototypeStyle.Accent);
                lootHeading.style.marginTop = 6;
                rewards.Add(lootHeading);
                rewards.Add(row);
            }
            if (choices.Count > 1)
            {
                var label = PrototypeStyle.Text(choices.All(item => item.BossExclusive) ? "首领专属战利品 · 2 选 1" : $"战利品 · {choices.Count} 选 1", 14, PrototypeStyle.Muted);
                label.name = "loot-choice-count";
                PrototypeStyle.Middle(label);
                rewards.Add(label);
                rewards.Add(choiceRow);
            }

            if (!victoryRewards.HasUnclaimed && choices.Count == 0)
                panel.Add(PrototypeStyle.Button("继续", "continue-after-ratings", ContinueVictory, true));
            topBar.BringRewardFlightsToFront();

            void Claim(Button source, Func<bool> grant)
            {
                if (topBar.CollectReward(source, grant))
                    ContinueVictory();
            }

            void ItemButton(VisualElement parent, EquipmentDefinition item, string id, Func<bool> grant)
            {
                var texture = content.IconFor(item.Id);
                string caption = item is SkillBookDefinition ? $"技能书 · {item.Name}" : item.Name;
                var button = RewardButton(caption, id, new EquipmentIcon(item, texture), source => Claim(source, grant));
                RegisterEquipmentTooltip(button, item, texture);
                parent.Add(button);
            }
        }

        private static VisualElement RewardRow()
        {
            var row = PrototypeStyle.Row();
            row.style.justifyContent = Justify.Center;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 8;
            return row;
        }

        private static Button RewardButton(string caption, string id, VisualElement icon, Action<Button> claim)
        {
            var button = PrototypeStyle.Button("", id, null);
            button.clicked += () => claim(button);
            button.style.backgroundColor = Color.clear;
            button.style.borderTopWidth = button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = button.style.borderRightWidth = 0;
            button.style.width = 110;
            button.style.minHeight = 106;
            button.style.marginLeft = button.style.marginRight = 4;
            button.style.marginBottom = 6;
            button.style.paddingLeft = button.style.paddingRight = 6;
            icon.style.alignSelf = Align.Center;
            icon.style.width = icon.style.height = 70;
            icon.style.flexGrow = 0;
            button.Add(icon);
            var label = PrototypeStyle.Text(caption, 13, PrototypeStyle.Accent);
            label.pickingMode = PickingMode.Ignore;
            PrototypeStyle.Middle(label);
            button.Add(label);
            return button;
        }

        private void RenderDefeat()
        {
            var panel = SettlementPanel("defeat-ratings", "defeat-summary", OutcomeText(result.Outcome));
            var summary = new ScrollView(ScrollViewMode.Vertical);
            summary.style.flexShrink = 1;
            panel.Add(summary);
            AddBattleRatings(summary);
            summary.Add(PrototypeStyle.Text("剩余生命  " + string.Join("  ·  ", run.Members.Select(member =>
                $"{member.Character.Name} {HealthText(member.Health, member.EffectiveStats.Health)}")), 14));
            if (result.GoldEarned > 0)
                summary.Add(PrototypeStyle.Text($"获得 {result.GoldEarned} 金币 · 当前 {run.Gold} 金币", 13, PrototypeStyle.Accent));
            if (result.AccessoriesEarned > 0)
                summary.Add(PrototypeStyle.Text($"获得 {result.AccessoriesEarned} 件饰品，已放入共享背包", 13, PrototypeStyle.Accent));
            if (adventure?.Current is { } visit)
                summary.Add(PrototypeStyle.Text(visit.Message, 15, PrototypeStyle.Accent));
            if (TeamWiped)
                panel.Add(PrototypeStyle.Button("返回主菜单", "leave-battle-ratings", () => PrototypeSceneMenu.OpenStandalone("MainMenu"), true));
            else if (adventure == null)
                panel.Add(PrototypeStyle.Button("重置演练", "leave-battle-ratings", () => ResetDemo(reroll: true), true));
            else
                panel.Add(PrototypeStyle.Button("返回事件选择", "leave-battle-ratings", AdventureFlow.ReturnFromNode, true));
        }

        private static string OutcomeText(BattleOutcome outcome) => outcome switch
        {
            BattleOutcome.Victory => "胜利 · 敌方全灭", BattleOutcome.PartyDefeated => "失败 · 讨伐队全灭",
            BattleOutcome.MutualDefeat => "失败 · 双方全灭", BattleOutcome.Timeout => "失败 · 达到战斗时限",
            _ => outcome.ToString()
        };
    }
}
