using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidPartyManager.Presentation
{
    // A change of scenery can straddle a Unity scene unload. Own the printed pieces, meshes and inks
    // until the incoming scene can take the stage; the auditorium and camera seat stay put.
    internal sealed class DockSetSnapshot
    {
        private readonly GameObject root;
        private readonly List<Object> resources = new();
        private readonly Dictionary<(Material, Texture), Material> inks = new();
        private readonly Dictionary<Mesh, Mesh> patterns = new();
        private readonly List<Piece> pieces = new();
        private readonly List<MeshRenderer> soft = new();
        private readonly List<Transform> flown = new();
        private readonly List<Vector3> rigPositions = new();
        private readonly List<Performer> cast = new();
        private readonly MaterialPropertyBlock block = new();
        internal MeshRenderer Sky { get; }
        internal SceneryFlipbook Scenery { get; }
        internal void AnimateBackdrop() => Scenery?.Apply(Sky, block);
        internal MeshRenderer Floor { get; }
        internal bool Moon { get; }
        private readonly float wing;

        private sealed class Piece
        {
            internal Transform hinge;
            internal Vector3 foot;
        }
        private sealed class Performer
        {
            internal MeshRenderer ink;
            internal Vector3 position, scale;
            internal DockSet.Player player;
            internal MaterialPropertyBlock rest;
        }

        internal DockSetSnapshot(DockSet set, Bounds deck, bool castOnly = false)
        {
            wing = Mathf.Max(14, deck.extents.x + 3);
            root = new GameObject("Travelling dock set");
            Object.DontDestroyOnLoad(root);
            Moon = set.Moon;
            Scenery = set.Scenery;
            var hinges = new Dictionary<Vector2, Piece>();
            foreach (var source in set.Root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!source.enabled || source.forceRenderingOff || castOnly && !set.Cast.ContainsKey(source)) continue;
                if (!source.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
                var mesh = filter.sharedMesh;
                var copy = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer)).GetComponent<MeshRenderer>();
                copy.transform.SetParent(root.transform, false);
                copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                copy.transform.localScale = source.transform.lossyScale;
                if (!patterns.TryGetValue(mesh, out var pattern))
                    resources.Add(patterns[mesh] = pattern = Object.Instantiate(mesh));
                var ink = source.sharedMaterial;
                source.GetPropertyBlock(block);
                var texture = block.GetTexture("_MainTex");
                var keyInk = (ink, texture);
                if (!inks.TryGetValue(keyInk, out var printing))
                {
                    resources.Add(inks[keyInk] = printing = new Material(ink));
                    // Scene unloading walks native material references. An override in a property
                    // block alone does not keep the previous node's painted cloth resident.
                    if (texture != null) printing.mainTexture = texture;
                }
                copy.GetComponent<MeshFilter>().sharedMesh = pattern;
                copy.sharedMaterial = printing;
                copy.sortingOrder = source.sortingOrder;
                copy.SetPropertyBlock(block);
                if (source == set.Sky) Sky = copy;
                else if (source == set.Floor) Floor = copy;
                else if (set.Cast.TryGetValue(source, out var player))
                {
                    var rest = new MaterialPropertyBlock();
                    source.GetPropertyBlock(rest);
                    cast.Add(new Performer { ink = copy, player = player, position = copy.transform.position,
                        scale = copy.transform.lossyScale, rest = rest });
                }
                else if (set.Flown.Contains(source))
                {
                    flown.Add(copy.transform);
                    rigPositions.Add(copy.transform.position);
                }
                else if (set.Atmosphere.Contains(source)) soft.Add(copy);
                else
                {
                    var foot = source.transform.position;
                    // A counter, notice board or stack of crates folds as one assembled prop.
                    var key = set.HingeFor(source);
                    if (!hinges.TryGetValue(key, out var piece))
                    {
                        var hinge = new GameObject("Scenery hinge").transform;
                        hinge.SetParent(root.transform, false);
                        foot = new Vector3(key.x, deck.max.y + .015f, key.y);
                        hinge.position = foot;
                        hinges[key] = piece = new Piece { hinge = hinge, foot = foot };
                        pieces.Add(piece);
                    }
                    copy.transform.SetParent(piece.hinge, true);
                }
            }
        }

        internal void CastAt(float fractionOnstage, bool entering, float seconds)
        {
            float on = Mathf.Clamp01(fractionOnstage);
            for (int i = 0; i < cast.Count; i++)
            {
                var actor = cast[i];
                float progress = Mathf.SmoothStep(0, 1, Mathf.Clamp01(on * 1.19f - .05f * (i % 4)));
                var view = actor.ink;
                view.enabled = progress > .001f;
                if (!view.enabled) continue;
                var position = actor.position;
                position.x = Mathf.Lerp(actor.player.Right ? wing : -wing, position.x, progress);
                var scale = actor.scale;
                if (!entering) scale.x *= -1;
                bool walking = progress < .999f || !entering;
                if (walking && actor.player.Walk != null)
                {
                    int cel = Mathf.FloorToInt(seconds * 13 + i) % 6;
                    view.GetPropertyBlock(block);
                    block.SetTexture("_MainTex", actor.player.Walk);
                    block.SetVector("_MainTex_ST", new Vector4(1f/3, .5f, cel % 3 / 3f, .5f - cel / 3 * .5f));
                    block.SetColor("_Color", Color.white);
                    view.SetPropertyBlock(block);
                    // All player travel sheets are painted facing stage right. Seated cards may be mirrored.
                    if (actor.player.WalkHeight > 0)
                    {
                        scale.y = actor.player.WalkHeight;
                        scale.x = actor.player.Right == entering ? -scale.y : scale.y;
                        position.y += actor.player.WalkLift;
                    }
                    else if (actor.player.Seated) scale.x = Mathf.Abs(scale.x) * (entering ? 1 : -1);
                }
                else if (!walking) view.SetPropertyBlock(actor.rest);
                else position.y += .038f * Mathf.Abs(Mathf.Sin(seconds * 13 + i));
                view.transform.position = position;
                view.transform.localScale = scale;
            }
        }

        internal void Fly(float amount)
        {
            for (int i = 0; i < flown.Count; i++)
                flown[i].position = rigPositions[i] + Vector3.up * (amount * (wing + 2));
        }

        internal void Fold(float flat)
        {
            foreach (var piece in pieces)
            {
                piece.hinge.position = piece.foot;
                piece.hinge.rotation = Quaternion.Euler(flat * 89, 0, 0);
                piece.hinge.localScale = new Vector3(1, 1, Mathf.Lerp(1, .035f, flat));
            }
            foreach (var ink in soft) ink.enabled = flat < .01f;
        }

        internal void Mount(Transform face)
        {
            Floor.transform.SetParent(face, true);
            foreach (var piece in pieces) piece.hinge.SetParent(face, true);
        }

        internal void Unmount() => Mount(root.transform);

        internal void ShowFace(bool visible)
        {
            Floor.enabled = visible;
            ShowPieces(visible);
        }

        internal void HideCast() { foreach (var actor in cast) actor.ink.enabled = false; }
        internal void ShowPieces(bool show) { foreach (var piece in pieces) piece.hinge.gameObject.SetActive(show); }
        internal void Dispose()
        {
            root.SetActive(false);
            Object.Destroy(root);
            foreach (var resource in resources) Object.Destroy(resource);
        }
    }
}
