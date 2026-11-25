using UnityEngine;
using UnityEngine.Rendering;

namespace RaidPartyManager.Presentation
{
    // A separate sheet lets the wake reach neighbouring slots while the rider keeps her usual scale.
    internal sealed class RidingWave : MonoBehaviour
    {
        private static Texture2D atlas;
        private MeshRenderer water, riderInk;
        private Material waterInk;
        private MaterialPropertyBlock block;

        internal static float Envelope(float progress)
        {
            return Mathf.SmoothStep(0, 1, progress / .22f) * Mathf.SmoothStep(0, 1, (1 - progress) / .30f);
        }

        internal static void Hide(RidingWave wave)
        {
            if (wave == null || !wave.water.enabled) return;
            wave.water.enabled = false;
            GameAudio.Play(SoundCue.ActorDeepSeaWhisperWaveLand, .42f);
            wave.riderInk.GetPropertyBlock(wave.block);
            wave.block.SetFloat("_RideFoam", 0);
            wave.riderInk.SetPropertyBlock(wave.block);
        }

        // The caller first places the rider at her ordinary ground position for this frame.
        internal static void Place(ref RidingWave wave, MeshRenderer rider, Texture pose, float amount)
        {
            if (amount <= 0 || pose == null || pose.name != "DeepSeaWhisper-Walk")
            {
                Hide(wave);
                return;
            }

            if (atlas == null) atlas = Resources.Load<Texture2D>("DeepSeaWhisper-Wave");
            if (atlas == null) return;
            if (wave == null)
            {
                var sheet = new GameObject("DeepSeaWhisper wave", typeof(MeshFilter), typeof(MeshRenderer));
                sheet.layer = rider.gameObject.layer;
                sheet.transform.SetParent(rider.transform, false);
                sheet.GetComponent<MeshFilter>().sharedMesh = rider.GetComponent<MeshFilter>().sharedMesh;
                wave = sheet.AddComponent<RidingWave>();
                wave.block = new MaterialPropertyBlock();
                wave.water = sheet.GetComponent<MeshRenderer>();
                wave.water.enabled = false;
                wave.riderInk = rider;
                var ink = rider.sharedMaterial;
                if (ink.HasProperty("_DepthWrite"))
                {
                    wave.waterInk = new Material(ink) { renderQueue = ink.renderQueue - 1 };
                    wave.waterInk.SetFloat("_DepthWrite", 0);
                }
                wave.water.sharedMaterial = wave.waterInk != null ? wave.waterInk : ink;
                wave.water.sortingLayerID = rider.sortingLayerID;
                wave.water.sortingOrder = -3;
                wave.water.shadowCastingMode = ShadowCastingMode.Off;
                wave.water.receiveShadows = false;
            }

            // The ink occupies 146 of the rider's 176 pixels. The tail rests on the curl
            // at 83% of the water cel's width. Support both centred and foot-pivoted stage meshes.
            var bounds = rider.localBounds;
            float lift = (146f / 176) * amount;
            float width = Mathf.Lerp(.85f, 2.55f, amount);
            float height = lift * 1.17f;
            var tr = rider.transform;
            tr.position += tr.TransformVector(Vector3.up * (lift * bounds.size.y));
            wave.transform.localScale = new Vector3(width, height, 1);
            wave.transform.localPosition = new Vector3(bounds.center.x - width * (bounds.min.x + .83f * bounds.size.x),
                bounds.min.y * (1 - height) + (.045f - lift) * bounds.size.y, .015f);
            rider.GetPropertyBlock(wave.block);
            float foam = Mathf.SmoothStep(0, 1, amount / .22f);
            wave.block.SetTexture("_RideWaveTex", atlas);
            wave.block.SetVector("_RideWavePlacement", new Vector4(1 / width, 1 / height,
                .83f - .5f / width, (lift - .045f) / height));
            wave.block.SetFloat("_RideFoam", foam);
            rider.SetPropertyBlock(wave.block);
            wave.block.SetFloat("_RideFoam", 0);
            wave.block.SetTexture("_MainTex", atlas);
            wave.block.SetVector("_InkTexelSize", new Vector4(1f / atlas.width, 1f / atlas.height, atlas.width, atlas.height));
            var tint = wave.block.GetColor("_Color");
            tint.a *= foam;
            wave.block.SetColor("_Color", tint);
            wave.water.SetPropertyBlock(wave.block);
            if (!wave.water.enabled) GameAudio.Play(SoundCue.ActorDeepSeaWhisperWave, .42f);
            wave.water.enabled = true;
        }

        private void OnDestroy()
        {
            if (waterInk != null) Destroy(waterInk);
        }
    }
}
