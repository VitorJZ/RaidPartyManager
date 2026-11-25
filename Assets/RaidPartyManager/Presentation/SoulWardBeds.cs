using System.Linq;
using RaidPartyManager.Content;
using RaidPartyManager.Run;
using UnityEngine;

namespace RaidPartyManager.Presentation
{
    internal sealed partial class DockEventStage
    {
        private static readonly Vector3[] BedCentres = { new(-1.55f, 0, -1.52f), new(1.55f, 0, -3.08f) };
        private const float MattressTop = .64f;
        private sealed class Patient
        {
            internal string Id;
            internal MeshRenderer Figure;
            internal RidingWave Wave;
            internal Texture2D Walk, Reclining;
            internal Vector3 Start;
            internal float Elapsed, Approach;
            internal bool InBed, PlayBedCue;
        }
        private readonly Patient[] patients = new Patient[2];
        private bool PatientsMoving => patients.Any(p => p != null && !p.InBed);

        private void BuildWardBeds()
        {
            var wood = new Color(.56f,.34f,.21f);
            var linen = new Color(.83f,.77f,.62f);
            foreach (var centre in BedCentres)
            {
                Box("Ward cot frame", centre + Vector3.up * .41f, new Vector3(2.64f,.18f,.96f), art.timber, wood);
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        Box("Carved bedpost", centre + new Vector3(x * 1.27f,.39f,z * .46f), new Vector3(.12f,.78f,.12f), art.timber, wood);
                foreach (int side in new[] { -1, 1 })
                {
                    Box("Bedside stretcher", centre + new Vector3(0,.23f,side*.45f), new Vector3(2.52f,.07f,.07f), art.timber, wood);
                    Shape("Finial", PrimitiveType.Sphere, centre + new Vector3(-1.27f,.93f,.46f*side),
                        new Vector3(.15f,.17f,.15f), null, new Color(.67f,.51f,.28f), Surface.Solid);
                }
                Box("Cot headboard", centre + new Vector3(-1.27f,.70f,0), new Vector3(.12f,.49f,1.01f), art.timber, wood);
                Box("Linen mattress", centre + Vector3.up * (MattressTop - .08f), new Vector3(2.45f,.16f,.91f), null, linen);
                Box("Folded wool blanket", centre + new Vector3(.75f,MattressTop+.015f,0), new Vector3(.76f,.035f,.94f), null, new Color(.33f,.45f,.40f));
                for (int stripe = 0; stripe < 2; stripe++)
                    Box("Woven hem", centre + new Vector3(.42f+stripe*.06f, MattressTop+.035f,0),
                        new Vector3(.022f,.011f,.94f), null, new Color(.66f,.58f,.38f));
                Shape("Cot pillow", PrimitiveType.Sphere, centre + new Vector3(-.91f,MattressTop+.115f,0),
                    new Vector3(.66f,.30f,.76f), null, new Color(.92f,.86f,.71f), Surface.Solid);
                Shadow(centre, 2.8f, 1.1f);
            }
        }

        internal void ShowEventCompany(AdventureRun run, EncounterData content)
        {
            if (run.Current?.Event != AdventureEventKind.SoulWard) { ShowCompany(run.Party, content); return; }
            ShowCompany(run.Party, content, id => id != run.WardPatient(0) && id != run.WardPatient(1));
        }

        internal void SyncPatients(AdventureRun run, EncounterData content, bool walkIn)
        {
            var members = run.Party.Members;
            for (int i = 0; i < patients.Length; i++)
            {
                var id = run.WardPatient(i);
                var old = patients[i];
                if (old?.Id == id) continue;
                if (old != null) Object.Destroy(old.Figure.gameObject);
                patients[i] = null;
                var member = members.FirstOrDefault(m => m.Id == id);
                if (member == null) continue;
                int slot = member.Position ?? 0;
                var start = slot > 0 ? new Vector3(-1.83f-.86f*(slot-1),.8f,-5.3f+.42f*((slot-1)%2)) : new Vector3(-6.4f,.8f,-3.9f);
                var walk = content.BattleWalkFor(member.Character.Id);
                var reclining = Resources.Load<Texture2D>("WardPatients/" + member.Character.RosterNumber);
                var figure = Flat("Ward patient " + id, start, CompanySpriteSize, CompanySpriteSize, walk);
                float approach = Mathf.Max(.56f, Vector3.Distance(start, BedCentres[i]) / 3.8f);
                patients[i] = new Patient { Id = id, Figure = figure, Walk = walk, Reclining = reclining,
                    Start = start, Approach = approach, Elapsed = walkIn ? 0 : approach + .48f, PlayBedCue = walkIn };
            }
            ShowEventCompany(run, content);
            AnimatePatients();
        }

        private void AnimatePatients()
        {
            for (int i = 0; i < patients.Length; i++)
            {
                var p = patients[i];
                if (p == null || p.InBed) continue;
                p.Elapsed += Mathf.Min(Time.unscaledDeltaTime,.08f);
                var bed = BedCentres[i];
                var bedside = bed + new Vector3(-.48f,.8f,-.55f);
                if (p.Elapsed < p.Approach)
                {
                    float t = Mathf.SmoothStep(0,1,p.Elapsed / p.Approach);
                    p.Figure.transform.localPosition = Vector3.Lerp(p.Start,bedside,t);
                    SetFrame(p.Figure,p.Walk,AnimationCels.Count,AnimationCels.Loop(p.Elapsed,.54));
                    RidingWave.Place(ref p.Wave, p.Figure, p.Walk, RidingWave.Envelope(p.Elapsed / p.Approach));
                    continue;
                }
                RidingWave.Hide(p.Wave);
                if (p.PlayBedCue)
                {
                    GameAudio.Play(SoundCue.EventGraveTwinBedsLieDown, .48f);
                    p.PlayBedCue = false;
                }
                float settle = Mathf.SmoothStep(0,1,(p.Elapsed-p.Approach)/.45f);
                float width = 2.29f, height = width * p.Reclining.height / p.Reclining.width;
                var rest = bed + new Vector3(0,MattressTop + height*.48f+.06f,-.07f);
                // Keep the head against the pillow as the torso lowers onto the mattress.
                p.Figure.transform.localPosition = Vector3.Lerp(bedside + Vector3.up*.22f,rest,settle);
                p.Figure.transform.localRotation = Quaternion.Euler(0,0,Mathf.Lerp(-32,0,settle));
                p.Figure.transform.localScale = new Vector3(width,height,1);
                SetFrame(p.Figure,p.Reclining,1,0);
                p.InBed = settle >= 1;
            }
        }
    }
}
