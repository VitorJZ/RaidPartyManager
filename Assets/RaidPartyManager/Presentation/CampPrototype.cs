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
    public sealed class CampPrototype : MonoBehaviour
    {
        private static readonly (int Position, Vector2 Hip)[] Seats =
        {
            (3, new Vector2(0.35f, 0.36f)),
            (2, new Vector2(0.65f, 0.36f)),
            (4, new Vector2(0.225f, 0.50f)),
            (1, new Vector2(0.775f, 0.50f))
        };

        [SerializeField] private EncounterAsset encounter;
        [SerializeField] private Texture2D backdrop;
        [SerializeField] private Texture2D fireArtwork;

        private readonly List<(Image View, Vector2 Hip)> figures = new List<(Image, Vector2)>();
        private VisualElement root;
        private VisualElement stage;
        private Image fire;
        private EncounterData content;
        private PartyRun run;
        private Font localFont;
        private FontAsset localFontAsset;
        private AdventureRun adventure;
        private bool standaloneRested;
        private string restMessage;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            PrototypeStyle.ApplySystemFont(root, out localFont, out localFontAsset);
            content = encounter.ToData();
            adventure = AdventureFlow.ForScene("Camp");
            run = adventure?.Party ?? BattlePrototype.GetPlaySession(content);
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            Render();
        }

        private void OnDisable()
        {
            root?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            PrototypeStyle.ClearDocument(root, localFont, localFontAsset);
        }

        private void Render()
        {
            root.Clear();
            figures.Clear();
            root.style.backgroundColor = PrototypeStyle.Background;
            root.style.overflow = Overflow.Hidden;
            stage = new VisualElement { name = "camp-stage", pickingMode = PickingMode.Ignore };
            stage.style.position = Position.Absolute;
            stage.style.backgroundImage = new StyleBackground(backdrop);
            stage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            root.Add(stage);

            var party = run.CaptureFormation(Side.Party);
            foreach (var seat in Seats)
            {
                var member = party.FirstOrDefault(unit => unit.Position == seat.Position);
                if (member == null)
                    continue;
                var art = content.CampSpriteFor(member.Character.Id);
                var view = new Image
                {
                    name = $"camp-seat-{seat.Position}",
                    image = art,
                    scaleMode = ScaleMode.StretchToFill,
                    pickingMode = PickingMode.Ignore
                };
                view.style.position = Position.Absolute;
                view.style.scale = new Scale(new Vector3(seat.Position <= 2 ? -1f : 1f, 1f, 1f));
                figures.Add((view, seat.Hip));
                stage.Add(view);
            }

            fire = new Image
            {
                name = "camp-fire", image = fireArtwork, scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore
            };
            fire.style.position = Position.Absolute;
            stage.Add(fire);

            var header = PrototypeStyle.Row("camp-header");
            header.style.position = Position.Absolute;
            header.style.top = RunTopBar.Height + 7;
            header.style.left = header.style.right = 22;
            header.style.alignItems = Align.Center;
            if (adventure != null)
                header.Add(AdventureFlow.ReturnButton());
            var title = PrototypeStyle.Text("营地", 24, PrototypeStyle.Accent);
            title.style.marginLeft = 18;
            title.style.flexGrow = 1;
            header.Add(title);
            root.Add(header);
            RunTopBar.PlaceAtTop(root, new RunTopBar(root, run, content, OpenEditor, "open-camp-editor"));
            var rest = new VisualElement { name = "camp-rest-actions" };
            rest.style.position = Position.Absolute; rest.style.bottom = 20;
            rest.style.left = Length.Percent(30); rest.style.right = Length.Percent(30);
            var button = PrototypeStyle.Button("休息 · 恢复最大生命的 30%", "camp-rest", () =>
            {
                if (adventure != null) adventure.Rest();
                else { int healed = run.Rest(0.3); standaloneRested = true; restMessage = $"合计恢复 {healed} 点生命。"; }
                Render();
            }, true);
            button.style.minHeight = 42;
            button.SetEnabled(adventure != null ? !adventure.Current.Rested && !adventure.Current.Resolved : !standaloneRested);
            rest.Add(button);
            var message = PrototypeStyle.Text(adventure?.Current.Message ?? restMessage ?? "休息不会复活已倒下的队员。", 14, PrototypeStyle.Ink);
            message.style.backgroundColor = new Color(0, 0, 0, 0.65f); PrototypeStyle.Middle(message); rest.Add(message);
            root.Add(rest);
            FitStage();
        }

        private void OpenEditor()
        {
            RunTopBar.EditParty(root, run, content, Render, adventure == null ? BattlePrototype.MarkPartyEdited : null);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => FitStage();

        private void FitStage()
        {
            if (stage == null || backdrop == null || root.contentRect.width <= 0 || root.contentRect.height <= 0)
                return;
            float ratio = (float)backdrop.width / backdrop.height;
            float width = Mathf.Min(root.contentRect.width, root.contentRect.height * ratio);
            float height = width / ratio;
            stage.style.width = width;
            stage.style.height = height;
            stage.style.left = (root.contentRect.width - width) * 0.5f;
            stage.style.top = (root.contentRect.height - height) * 0.5f;
            foreach (var figure in figures)
            {
                float size = width * 0.22f;
                figure.View.style.width = figure.View.style.height = size;
                figure.View.style.left = width * figure.Hip.x - size * 0.5f;
                figure.View.style.top = height * figure.Hip.y - size * 0.56f;
            }
            float fireSize = width * 0.17f;
            fire.style.width = fire.style.height = fireSize;
            fire.style.left = width * 0.5f - fireSize * 0.5f;
            fire.style.top = height * 0.73f - fireSize * 0.82f;
        }

        private void Update()
        {
            if (fire?.panel == null)
                return;
            float time = Time.unscaledTime;
            fire.style.scale = new Scale(new Vector3(1f + Mathf.Sin(time * 6.3f) * 0.012f,
                1f + Mathf.Sin(time * 8.1f) * 0.025f, 1f));
            float light = 0.96f + Mathf.Sin(time * 7.3f) * 0.04f;
            fire.tintColor = new Color(1f, light, light, 1f);
        }
    }
}
