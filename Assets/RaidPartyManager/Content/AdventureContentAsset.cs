using RaidPartyManager.Run;
using UnityEngine;

namespace RaidPartyManager.Content
{
    [CreateAssetMenu(menuName = "Raid Party Manager/Adventure Content")]
    public sealed class AdventureContentAsset : ScriptableObject
    {
        [SerializeField] private EncounterAsset encounter;
        [SerializeField] private Texture2D backdrop;
        [SerializeField] private Texture2D townBackdrop;
        [SerializeField] private Texture2D eventParchment;
        [SerializeField] private Texture2D hat;
        [SerializeField] private Texture2D[] eventArtwork;
        [SerializeField] private Texture2D[] eventInkArtwork;
        [SerializeField] private Shader eventInkShader;
        [SerializeField] private Texture2D[] nodeIcons;

        public EncounterData Encounter => encounter.ToData();
        public Texture2D Backdrop => backdrop;
        public Texture2D TownBackdrop => townBackdrop;
        public Texture2D EventParchment => eventParchment;
        public Texture2D Hat => hat;
        public Texture2D EventArt(AdventureEventKind kind) => (int)kind < eventArtwork.Length ? eventArtwork[(int)kind] : null;
        public Shader EventInkShader => eventInkShader;
        public Texture2D EventInkArt(AdventureEventKind kind)
        {
            int index = (int)kind;
            return eventInkArtwork != null && index >= 0 && index < eventInkArtwork.Length ? eventInkArtwork[index] : null;
        }
        public Texture2D NodeIcon(NodeKind kind)
        {
            int index = kind switch { NodeKind.Event => 0, NodeKind.Town => 1, NodeKind.Camp => 2,
                NodeKind.Shop => 3, NodeKind.NormalBattle => 4, _ => 5 };
            return index < nodeIcons.Length ? nodeIcons[index] : null;
        }
    }
}
