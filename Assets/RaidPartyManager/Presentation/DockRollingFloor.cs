using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidPartyManager.Presentation
{
    // The centreline runs from the midpoint of the apron to the back of the half-disc (+Z).
    // Both prints and the timber between them ride one shaft. Positive roll lifts audience-right.
    internal sealed class DockRollingFloor
    {
        private readonly Transform shaft, underside;
        private readonly MeshRenderer stationaryFrame;
        private readonly DockSetSnapshot outgoing, incoming;

        internal DockRollingFloor(MeshRenderer frame, Transform machinery, DockSetSnapshot outgoing, DockSetSnapshot incoming)
        {
            this.outgoing = outgoing;
            this.incoming = incoming;
            stationaryFrame = frame;
            Bounds bounds = frame.bounds;
            shaft = new GameObject("Semicircular deck shaft").transform;
            shaft.SetParent(machinery, false);
            shaft.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);
            underside = new GameObject("Reverse face").transform;
            underside.SetParent(shaft, false);

            // Copy the actual apron-facing fascia and underside joists so the edge has depth at 90 degrees.
            var timber = new GameObject("Revolving deck and joists", typeof(MeshFilter), typeof(MeshRenderer));
            timber.transform.SetPositionAndRotation(frame.transform.position, frame.transform.rotation);
            timber.transform.localScale = frame.transform.lossyScale;
            timber.transform.SetParent(shaft, true);
            timber.GetComponent<MeshFilter>().sharedMesh = frame.GetComponent<MeshFilter>().sharedMesh;
            timber.GetComponent<MeshRenderer>().sharedMaterial = frame.sharedMaterial;
            frame.forceRenderingOff = true;

            outgoing.Mount(shaft);
            incoming.Mount(underside);
            underside.localRotation = Quaternion.AngleAxis(180, Vector3.forward);
            Turn(0);
        }

        internal void Turn(float degrees)
        {
            shaft.localRotation = Quaternion.AngleAxis(degrees, Vector3.forward);
            // Both printing shaders render double-sided. The front row sees one face at a time.
            bool reverse = degrees >= 90;
            outgoing.ShowFace(!reverse);
            incoming.ShowFace(reverse);
        }

        internal void Dispose()
        {
            outgoing.Unmount();
            incoming.Unmount();
            stationaryFrame.forceRenderingOff = false;
            shaft.gameObject.SetActive(false);
            Object.Destroy(shaft.gameObject);
        }
    }
}
