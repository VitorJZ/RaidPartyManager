using RaidPartyManager.Domain;
using RaidPartyManager.Run;
using UnityEngine;
using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.PrototypeText;

namespace RaidPartyManager.Presentation
{
    internal sealed class CharacterChoicePreview : VisualElement
    {
        private const float MaxWidth = 316;

        private CharacterChoicePreview(CharacterDefinition character, Texture2D art, MemberView member)
        {
            name = "character-choice-preview";
            style.position = Position.Absolute;
            style.paddingTop = style.paddingBottom = 10;
            style.paddingLeft = style.paddingRight = 12;
            style.backgroundColor = (Color)new Color32(31, 33, 43, 255);
            PrototypeStyle.Outline(this, PrototypeStyle.Accent, 6);

            var headline = PrototypeStyle.Row();
            headline.style.alignItems = Align.Center;
            var portrait = new Image { image = art, scaleMode = ScaleMode.ScaleToFit };
            portrait.style.width = 67;
            portrait.style.height = 88;
            portrait.style.flexShrink = 0;
            portrait.style.marginRight = 9;
            if (art != null)
                headline.Add(portrait);
            var heading = new VisualElement();
            heading.style.flexGrow = 1;
            heading.style.flexBasis = 0;
            AddText(heading, "name", character.Name, 18, PrototypeStyle.Accent);
            bool melee = character.Reach == AttackReach.Melee ||
                character.Reach == AttackReach.Unspecified && character.Profession == Profession.Warrior;
            string reach = melee ? "近战" : "远程";
            AddText(heading, "type", $"{AdventureFlow.ProfessionName(character.Profession)} · {reach} · {character.Stage} 阶", 12);
            var stats = member?.EffectiveStats ?? character.BaseStats;
            string hp = member == null ? stats.Health.ToString() : HealthText(member.Health, stats.Health);
            AddText(heading, "health", $"生命 {hp}", 13);
            AddText(heading, "stats", $"攻击 {stats.Attack}   防御 {stats.Defense}   特防 {stats.SpecialDefense}", 13);
            headline.Add(heading);
            Add(headline);

            var voice = EmployeeVoice.For(member?.Personality ?? character.Personality);
            var personality = AddText(this, "personality",
                $"{voice.Name} · {voice.Description}", 12);
            personality.style.marginTop = 7;
            if (!string.IsNullOrWhiteSpace(character.TraitText))
            {
                var trait = AddText(this, "trait", "被动 · " + character.TraitText, 13);
                trait.style.marginTop = 5;
            }
            IgnorePicking(this);
        }

        public static void Attach(VisualElement anchor, CharacterDefinition character, Texture2D art, MemberView member = null)
        {
            CharacterChoicePreview shown = null;
            anchor.RegisterCallback<PointerEnterEvent>(evt =>
            {
                if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse || anchor.panel == null)
                    return;
                Hide();
                var host = anchor;
                while (host.parent != null && host.parent != anchor.panel.visualTree)
                    host = host.parent;

                var preview = new CharacterChoicePreview(character, art, member);
                shown = preview;
                preview.RegisterCallback<GeometryChangedEvent>(_ => preview.Place(host, anchor));
                host.Add(preview);
                preview.Place(host, anchor);
            });
            anchor.RegisterCallback<PointerLeaveEvent>(_ => Hide());
            anchor.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                    Hide();
            }, TrickleDown.TrickleDown);
            anchor.RegisterCallback<NavigationSubmitEvent>(_ => Hide(), TrickleDown.TrickleDown);
            anchor.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                var preview = shown;
                shown = null;
                if (preview?.parent is not { } host || host.IndexOf(preview) < 0)
                    return;

                // The document may be clearing several sibling elements in this detach callback.
                preview.style.display = DisplayStyle.None;
                host.schedule.Execute(() => RemovePreview(preview));
            });

            void Hide()
            {
                var preview = shown;
                shown = null;
                RemovePreview(preview);
            }
        }

        private static void RemovePreview(CharacterChoicePreview preview)
        {
            if (preview?.parent is { } host && host.IndexOf(preview) >= 0)
                preview.RemoveFromHierarchy();
        }

        private void Place(VisualElement host, VisualElement anchor)
        {
            const float gap = 8;
            float width = Mathf.Clamp(host.layout.width - 2 * gap, 0, MaxWidth);
            float height = float.IsNaN(layout.height) ? 0f : layout.height;
            style.width = width;
            var bounds = host.WorldToLocal(anchor.worldBound);
            float left = bounds.xMax + gap;
            float top = bounds.center.y - height * 0.5f;
            if (left + width > host.layout.width - gap)
                left = bounds.xMin - width - gap;
            if (left < gap)
            {
                left = bounds.center.x - width * 0.5f;
                top = bounds.yMin - height - gap;
                if (top < gap)
                    top = bounds.yMax + gap;
            }

            style.left = Mathf.Clamp(left, gap, Mathf.Max(gap, host.layout.width - width - gap));
            style.top = Mathf.Clamp(top, gap, Mathf.Max(gap, host.layout.height - height - gap));
        }

        private static Label AddText(VisualElement parent, string field, string text, int size, Color? color = null)
        {
            var label = PrototypeStyle.Text(text, size, color);
            label.name = "character-preview-" + field;
            parent.Add(label);
            return label;
        }

        private static void IgnorePicking(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (var child in element.Children())
                IgnorePicking(child);
        }
    }
}
