using System.Collections.Generic;
using UnityEngine;

namespace RaidPartyManager.Presentation
{
    // A node dresses a common house. These references also identify which pieces leave with the cast.
    internal sealed class DockSet
    {
        internal readonly Transform Root;
        internal MeshRenderer Sky, Floor;
        internal SceneryFlipbook Scenery;
        internal readonly Dictionary<MeshRenderer, Player> Cast = new();
        internal readonly HashSet<MeshRenderer> Flown = new();
        internal readonly List<DockRoadpost> Roadposts = new();
        internal readonly HashSet<MeshRenderer> Atmosphere = new();
        internal bool Moon, NativeEntrance, RoadstonesLanded;
        internal StageLighting Lighting;
        internal readonly Dictionary<MeshRenderer, Vector2> Hinges = new();
        internal Vector2 HingeFor(MeshRenderer mesh) => Hinges.TryGetValue(mesh, out var hinge) ? hinge :
            new Vector2(mesh.transform.position.x, mesh.transform.position.z);
        internal DockSet(Transform root) => Root = root;

        internal readonly struct Player
        {
            internal readonly bool Right, Seated;
            internal readonly Texture2D Walk, Idle;
            internal readonly float WalkHeight, WalkLift;
            internal Player(bool right, Texture2D walk = null, Texture2D idle = null, bool seated = false,
                float walkHeight = 0, float walkLift = 0)
            { Right = right; Walk = walk; Idle = idle; Seated = seated; WalkHeight = walkHeight; WalkLift = walkLift; }
        }
    }
}
