using System.Collections.Generic;
using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class TalentMarket : MonoBehaviour
    {
        private const string NoCandidates = "本次挂牌的队员已全部签约。";
        [SerializeField] private AdventureContentAsset assets;
        [SerializeField] private ServiceStageArt stageArt;
        private TalentMarketStage stage;
        private AdventureRun run;
        private VisualElement root;
        private RunTopBar topBar;
        private (Button Entry, Label Price)[] marketTargets;
        private Label profile;
        private Button profileAnchor, brokerBubble;
        private MarketDialogue dialogue;
        private bool returnRequested;
        private Font font;
        private FontAsset fontAsset;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            PrototypeStyle.ApplySystemFont(root, out font, out fontAsset);
            run = AdventureFlow.TalentMarketRun(assets);
            var candidates = run.Current.Recruits.Where(c => run.Party.Members.All(m => m.Character.Id != c.Id)).ToArray();
            stage = new TalentMarketStage(transform, root, stageArt, candidates, assets.Encounter, run.InGraveyard);
            returnRequested = false;
            root.style.backgroundColor = Color.clear;
            marketTargets = new (Button Entry, Label Price)[candidates.Length];
            topBar = new RunTopBar(root, run.Party, assets.Encounter, () => topBar.EditParty(topBar.Refresh), "open-market-editor");
            RunTopBar.PlaceAtTop(root, topBar);
            BuildMarket(candidates);
        }

        private void Update()
        {
            stage.FrameCamera();
            stage.Animate(Time.unscaledTime);
            for (int i = 0; i < marketTargets.Length; i++)
            {
                var (entry, price) = marketTargets[i];
                if (entry != null) stage.PlaceCandidate(entry, price, i);
            }
            if (brokerBubble != null)
            {
                var head = stage.BrokerHead;
                brokerBubble.style.left = Mathf.Clamp(head.x * root.layout.width - 71, 4, Mathf.Max(4, root.layout.width - 146));
                brokerBubble.style.top = Mathf.Max(RunTopBar.Height + 4, (1 - head.y) * root.layout.height - 42);
            }
            if (profileAnchor != null) PlaceProfile();
            if (returnRequested && !stage.HasDepartures) Leave();
        }

        private void OnDisable()
        {
            stage?.Dispose();
            PrototypeStyle.ClearDocument(root, font, fontAsset);
        }

        private void Leave()
        {
            returnRequested = stage.HasDepartures;
            if (!returnRequested) topBar.WhenRewardsArrive(AdventureFlow.ReturnFromNode);
        }

        private void BuildMarket(IReadOnlyList<CharacterDefinition> candidates)
        {
            var overlay = new VisualElement { name = "town-talent-market" };
            PrototypeStyle.Fill(overlay);
            var panel = new VisualElement { pickingMode = PickingMode.Ignore };
            PrototypeStyle.Fill(panel);
            overlay.Add(panel);
            var header = PrototypeStyle.Row();
            header.style.position = Position.Absolute; header.style.top = RunTopBar.Height + 8;
            header.style.left = header.style.right = Length.Percent(10);
            header.style.alignItems = Align.Center;
            var title = PrototypeStyle.Text("人才市场", 24, PrototypeStyle.Accent);
            header.style.justifyContent = Justify.SpaceBetween;
            title.style.backgroundColor = PrototypeStyle.PanelColor;
            title.style.paddingLeft = title.style.paddingRight = 10;
            header.Add(title);
            header.Add(PrototypeStyle.Button("返回城镇", "close-town-recruit", Leave));
            var empty = PrototypeStyle.Text(NoCandidates, 15, PrototypeStyle.Accent);
            empty.style.display = candidates.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            empty.style.position = Position.Absolute;
            empty.style.top = RunTopBar.Height + 52;
            empty.style.left = Length.Percent(10);
            empty.style.backgroundColor = PrototypeStyle.PanelColor;
            empty.pickingMode = PickingMode.Ignore;
            profile = PrototypeStyle.Text("", 15, PrototypeStyle.Ink);
            profile.name = "market-candidate-profile";
            profile.style.position = Position.Absolute;
            profile.style.paddingLeft = profile.style.paddingRight = 12;
            profile.style.paddingTop = profile.style.paddingBottom = 8;
            profile.style.backgroundColor = PrototypeStyle.PanelColor;
            profile.pickingMode = PickingMode.Ignore;
            PrototypeStyle.Outline(profile, PrototypeStyle.Edge, 4);
            HideProfile();
            for (int i = 0; i < candidates.Count; i++)
            {
                int index = i;
                var c = candidates[i];
                var trait = run.Party.TalentFor(c);
                int signingPrice = run.Party.SigningPrice(c);
                int expectedWage = CrewContract.ExpectedWage(c, trait);
                string info = $"{c.Name}　{AdventureFlow.ProfessionName(c.Profession)}　{c.Stage} 阶　" +
                    $"攻击 {c.BaseStats.Attack}　防御 {c.BaseStats.Defense}　特防 {c.BaseStats.SpecialDefense}　生命 {c.BaseStats.Health}\n" +
                    $"期望日薪 {expectedWage} 金币\n" + c.TraitText + "　" + PlayerMarket.Describe(trait) + "\n性格 · " + EmployeeVoice.For(c.Personality).Name;
                var entry = PrototypeStyle.Button("", "town-recruit-" + c.Id, () => {
                    if (run.Party.Members.Count >= PartyRun.RosterLimit)
                    {
                        ShowProfile(marketTargets[index].Entry, info + "\n队伍人数已满。");
                        return;
                    }
                    HideProfile();
                    VisualElement contract = null;
                    contract = ContractPanel.Negotiate(c, run.Party.Day, trait, signingPrice, null, expectedWage,
                        (days, terms, clause, wage) =>
                        {
                            if (!run.ChooseTownRecruit(c.Id, days, terms, clause, wage)) return false;
                            GameAudio.Play(SoundCue.CompanionJoin);

                            stage.BeginDeparture(index, Time.unscaledTime);
                            marketTargets[index].Entry.RemoveFromHierarchy();
                            marketTargets[index].Price.RemoveFromHierarchy();
                            marketTargets[index] = default;
                            topBar.Refresh();
                            if (marketTargets.All(target => target.Entry == null)) empty.style.display = DisplayStyle.Flex;
                            return true;
                        },
                        () => contract.RemoveFromHierarchy());
                    overlay.Add(contract);
                });
                entry.style.backgroundColor = Color.clear;
                PrototypeStyle.Outline(entry, Color.clear, 0);
                var price = PrototypeStyle.Text($"价格 {signingPrice}", 13, new Color32(65, 38, 21, 255));
                price.name = "town-price-" + c.Id;
                price.pickingMode = PickingMode.Ignore;
                price.style.unityTextAlign = TextAnchor.MiddleCenter;
                price.style.unityFontStyleAndWeight = FontStyle.Bold;
                price.style.whiteSpace = WhiteSpace.NoWrap;
                marketTargets[i] = (entry, price);
                stage.PlaceCandidate(entry, price, i);
                entry.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse) ShowProfile(entry, info);
                });
                entry.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (profileAnchor == entry) HideProfile();
                });
                panel.Add(entry);
                panel.Add(price);
            }
            brokerBubble = PrototypeStyle.Button("要做比交易吗？", "market-broker", () =>
            {
                if (dialogue != null || !run.BrokerOpen) return;
                HideProfile();
                stage.ShowBroker(false);
                overlay.style.display = DisplayStyle.None;
                topBar.style.display = DisplayStyle.None;
                dialogue = new MarketDialogue(run, topBar.Refresh, () =>
                {
                    dialogue.RemoveFromHierarchy();
                    dialogue = null;
                    stage.ShowBroker(true);
                    overlay.style.display = DisplayStyle.Flex;
                    topBar.style.display = DisplayStyle.Flex;
                    UpdateBrokerBubble();
                    topBar.Refresh();
                });
                root.Add(dialogue);
            });
            brokerBubble.style.position = Position.Absolute;
            brokerBubble.style.width = 142;
            brokerBubble.style.height = 34;
            brokerBubble.style.backgroundColor = (Color)new Color32(255, 241, 212, 255);
            brokerBubble.style.color = (Color)new Color32(70, 38, 44, 255);
            PrototypeStyle.Outline(brokerBubble, new Color32(123, 84, 58, 255), 14);
            var tail = new VisualElement { pickingMode = PickingMode.Ignore };
            tail.style.position = Position.Absolute;
            tail.style.width = tail.style.height = 8;
            tail.style.bottom = -5;
            tail.style.left = Length.Percent(46);
            tail.style.rotate = new Rotate(new Angle(45));
            tail.style.backgroundColor = brokerBubble.style.backgroundColor;
            brokerBubble.Add(tail);
            UpdateBrokerBubble();
            panel.Add(brokerBubble);
            panel.Add(header);
            panel.Add(empty);
            panel.Add(profile);
            root.Insert(0, overlay);
        }

        private void UpdateBrokerBubble()
        {
            brokerBubble.text = run.BrokerOpen ? "要做比交易吗？" : "今天收摊啦。";
            brokerBubble.SetEnabled(run.BrokerOpen);
        }

        private void ShowProfile(Button anchor, string info)
        {
            profileAnchor = anchor;
            profile.text = info;
            profile.style.display = DisplayStyle.Flex;
            PlaceProfile();
        }

        private void HideProfile()
        {
            profileAnchor = null;
            profile.style.display = DisplayStyle.None;
        }

        private void PlaceProfile()
        {
            const float margin = 8;
            var bounds = root.WorldToLocal(profileAnchor.worldBound);
            float width = Mathf.Max(0, Mathf.Min(330, root.layout.width - margin * 2));
            profile.style.width = width;
            profile.style.left = Mathf.Clamp(bounds.center.x - width * .5f, margin,
                Mathf.Max(margin, root.layout.width - width - margin));
            profile.style.bottom = root.layout.height - bounds.yMin + margin;
        }
    }
}
