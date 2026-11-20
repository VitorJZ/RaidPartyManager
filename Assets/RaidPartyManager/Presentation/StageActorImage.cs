using UnityEngine;
using UnityEngine.UIElements;

namespace RaidPartyManager.Presentation
{
    // The panel supplies the pose, layout and hit target. The camera draws the ink at that position.
    internal sealed class StageActorImage : Image
    {
        private readonly bool drawnOnStage;
        public Color Ink { get; private set; } = Color.white;
        public Vector4 Walk { get; set; }
        internal DockSet.Player Performer { get; set; }
        public bool InMotion { get; }

        public StageActorImage(BattleStage stage, bool inMotion = false)
        {
            InMotion = inMotion;
            if (stage == null)
                return;
            drawnOnStage = true;
            tintColor = Color.clear;
            RegisterCallback<AttachToPanelEvent>(_ => stage.Attach(this));
            RegisterCallback<DetachFromPanelEvent>(_ => stage.Detach(this));
        }

        public void SetInk(Color color)
        {
            Ink = color;
            if (!drawnOnStage)
                tintColor = color;
        }
    }
}
