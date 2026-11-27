using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RaidPartyManager.Presentation
{
    // The set owns this pass. An event battle sharing that set also shares its lens.
    internal sealed class StageTiltShift : ScriptableRenderPass, IDisposable
    {
        internal const float FacilityStrength = .32f;
        internal const uint SharpLayer = 1u << 8; // Stage Lens Sharp in the project's rendering layers.

        private static readonly ShaderTagId LensMaskPass = new("StageLensMask");
        private static readonly int LensMask = Shader.PropertyToID("_StageLensMask");
        private readonly Camera stageCamera;
        private readonly Material lens;
        private readonly Func<Bounds> formation;
        private static readonly int FocusAxis = Shader.PropertyToID("_FocusAxis");
        private static readonly int SharpBand = Shader.PropertyToID("_SharpBand");

        internal StageTiltShift(Camera camera, Func<Bounds> formation, float strength = 1f)
        {
            stageCamera = camera;
            this.formation = formation;
            lens = CoreUtils.CreateEngineMaterial(Resources.Load<Shader>("StageTiltShift"));
            lens.SetFloat("_BlurStrength", strength);
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            requiresIntermediateTexture = true;
            RenderPipelineManager.beginCameraRendering += RenderStage;
        }

        private void RenderStage(ScriptableRenderContext context, Camera camera)
        {
            if (camera != stageCamera || GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset pipeline)
                return;
            var renderer = camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)
                ? data.scriptableRenderer : pipeline.scriptableRenderer;
            renderer.EnqueuePass(this);
        }

        private bool ProjectFormation()
        {
            var bounds = formation();
            var foot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var mark = stageCamera.WorldToViewportPoint(foot);
            var next = stageCamera.WorldToViewportPoint(foot + Vector3.right);
            if (mark.z <= stageCamera.nearClipPlane || next.z <= stageCamera.nearClipPlane) return false;

            // Measure perpendicular distances in viewport heights, even on a wide screen.
            float aspect = stageCamera.aspect;
            var normal = new Vector2(mark.y - next.y, (next.x - mark.x) * aspect).normalized;
            if (normal == Vector2.zero) return false;
            if (normal.y < 0) normal = -normal;
            var axis = new Vector2(normal.x * aspect, normal.y);
            float origin = Vector2.Dot(axis, mark);
            lens.SetVector(FocusAxis, new Vector4(axis.x, axis.y, -origin, 0));

            // Include both rows of a staggered company and the full height of its paper actors.
            var near = foot - Vector3.forward * bounds.extents.z;
            var far = foot + Vector3.forward * bounds.extents.z;
            var up = Vector3.up * bounds.size.y;
            float bottom = Mathf.Min(Offset(near), Offset(far));
            float top = Mathf.Max(Offset(near + up), Offset(far + up));
            lens.SetVector(SharpBand, new Vector4(bottom - .012f, top + .012f, 0, 0));
            return true;

            float Offset(Vector3 point) => Vector2.Dot(axis, stageCamera.WorldToViewportPoint(point)) - origin;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var resources = frame.Get<UniversalResourceData>();
            var source = resources.activeColorTexture;
            if (resources.isActiveTargetBackBuffer || !source.IsValid() || !ProjectFormation()) return;

            var descriptor = graph.GetTextureDesc(source);
            RecordSharpMask(graph, frame, descriptor);
            descriptor.clearBuffer = false;
            descriptor.name = "Stage lens horizontal";
            var horizontal = graph.CreateTexture(descriptor);
            descriptor.name = "Stage lens colour";
            var result = graph.CreateTexture(descriptor);
            RecordBlur(graph, source, horizontal, 0, "Stage tilt-shift horizontal");
            RecordBlur(graph, horizontal, result, 1, "Stage tilt-shift vertical");
            resources.cameraColor = result;
        }

        private void RecordSharpMask(RenderGraph graph, ContextContainer frame, TextureDesc descriptor)
        {
            descriptor.name = "Stage lens sharp silhouettes";
            descriptor.colorFormat = GraphicsFormat.R8_UNorm;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            var mask = graph.CreateTexture(descriptor);

            var sorting = new SortingSettings(stageCamera) { criteria = SortingCriteria.CommonTransparent };
            var drawing = new DrawingSettings(LensMaskPass, sorting) { perObjectData = PerObjectData.None };
            var filter = new FilteringSettings(RenderQueueRange.all, renderingLayerMask: SharpLayer);
            var culling = frame.Get<UniversalRenderingData>().cullResults;
            using var builder = graph.AddRasterRenderPass<MaskPassData>("Stage lens sharp silhouettes", out var pass);
            pass.renderers = graph.CreateRendererList(new RendererListParams(culling, drawing, filter));
            builder.UseRendererList(pass.renderers);
            builder.SetRenderAttachment(mask, 0);
            var depth = frame.Get<UniversalResourceData>().activeDepthTexture;
            if (depth.IsValid()) builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
            builder.SetGlobalTextureAfterPass(mask, LensMask);
            builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                context.cmd.DrawRendererList(data.renderers));
        }

        private void RecordBlur(RenderGraph graph, TextureHandle source, TextureHandle destination, int shaderPass, string name)
        {
            using var builder = graph.AddRasterRenderPass<BlurPassData>(name, out var pass);
            pass.source = source;
            pass.lens = lens;
            pass.shaderPass = shaderPass;
            builder.UseTexture(source);
            builder.UseGlobalTexture(LensMask);
            builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
            builder.SetRenderFunc(static (BlurPassData data, RasterGraphContext context) =>
                Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.lens, data.shaderPass));
        }

        private sealed class MaskPassData
        {
            internal RendererListHandle renderers;
        }

        private sealed class BlurPassData
        {
            internal TextureHandle source;
            internal Material lens;
            internal int shaderPass;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= RenderStage;
            CoreUtils.Destroy(lens);
        }
    }
}
