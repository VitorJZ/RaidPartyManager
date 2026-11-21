using UnityEngine.UIElements;
using static RaidPartyManager.Presentation.SoundCue;

namespace RaidPartyManager.Presentation
{
    internal static class InterfaceSounds
    {
        internal static void Bind(VisualElement root)
        {
            root.RegisterCallback<PointerEnterEvent>(Hover, TrickleDown.TrickleDown);
            root.RegisterCallback<ClickEvent>(Click, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(Submit, TrickleDown.TrickleDown);
        }

        internal static void Unbind(VisualElement root)
        {
            root.UnregisterCallback<PointerEnterEvent>(Hover, TrickleDown.TrickleDown);
            root.UnregisterCallback<ClickEvent>(Click, TrickleDown.TrickleDown);
            root.UnregisterCallback<NavigationSubmitEvent>(Submit, TrickleDown.TrickleDown);
        }

        private static void Hover(PointerEnterEvent evt)
        {
            if (evt.target is Button b && b.enabledInHierarchy && b.name != "auction-floor")
                GameAudio.Play(UiHover, .46f);
        }

        private static void Click(ClickEvent evt)
        {
            if (evt.button == 0) Press(evt.target as VisualElement);
        }

        private static void Submit(NavigationSubmitEvent evt) => Press(evt.target as VisualElement);

        private static void Press(VisualElement target)
        {
            var button = target as Button ?? target?.GetFirstAncestorOfType<Button>();
            if (button == null) return;
            var id = button.name ?? "";
            var cue = UiConfirm;
            if (!button.enabledInHierarchy) cue = UiRestricted;
            else if (id.StartsWith("close-") || id.StartsWith("return-") || id.StartsWith("back-") ||
                id.StartsWith("cancel-") || id == "auction-floor") cue = UiCancel;
            else if (id.Contains("-tab") || id.Contains("-page") || id is "bag-previous" or "bag-next" || id.Contains("bookmark")) cue = UiPageTurn;
            else if (id.StartsWith("starter-") || id.StartsWith("town-recruit-") || id.StartsWith("evolve-")) cue = UiCardSelect;
            else if (id.Contains("contract") || id.Contains("open-") && id.Contains("editor")) cue = UiParchmentOpen;
            GameAudio.Play(cue, .50f);
        }
    }
}
