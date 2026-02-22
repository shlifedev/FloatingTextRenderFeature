using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LD.FloatingTextRenderFeature
{
    public class FloatingTextRenderFeature : ScriptableRendererFeature
    {
        private FloatingTextRenderPass _pass;

        public override void Create()
        {
            _pass = new FloatingTextRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var mgr = FloatingTextManager.Instance;
            if (mgr == null) return;
            if (mgr.CharCounts == null) return;

            renderer.EnqueuePass(_pass);
        }

        private class FloatingTextRenderPass : ScriptableRenderPass
        {
            private class PassData
            {
                internal Mesh mesh;
                internal Material[] materials;
                internal Matrix4x4[][] matrices;
                internal int[] counts;
                internal int charCount;
                internal TextureHandle colorTarget;
                internal TextureHandle depthTarget;

                // Atlas mode
                internal bool useAtlas;
                internal Material atlasMaterial;
                internal Matrix4x4[] atlasMatrices;
                internal int atlasCount;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var mgr = FloatingTextManager.Instance;
                if (mgr == null) return;
                if (mgr.QuadMesh == null || mgr.CharCounts == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();

                using (var builder = renderGraph.AddUnsafePass<PassData>("FloatingText", out var passData))
                {
                    passData.mesh = mgr.QuadMesh;
                    passData.useAtlas = mgr.UseAtlas;
                    passData.colorTarget = resourceData.activeColorTexture;
                    passData.depthTarget = resourceData.activeDepthTexture;

                    if (mgr.UseAtlas)
                    {
                        passData.atlasMaterial = mgr.AtlasMaterial;
                        passData.atlasMatrices = mgr.AtlasMatrices;
                        passData.atlasCount = mgr.AtlasCount;
                    }
                    else
                    {
                        passData.materials = mgr.MaterialArray;
                        passData.matrices = mgr.CharMatrices;
                        passData.counts = mgr.CharCounts;
                        passData.charCount = mgr.SupportedCharCount;
                    }

                    builder.UseTexture(passData.colorTarget, AccessFlags.Write);
                    builder.UseTexture(passData.depthTarget, AccessFlags.Write);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                    {
                        context.cmd.SetRenderTarget(data.colorTarget, data.depthTarget);

                        if (data.useAtlas)
                        {
                            if (data.atlasCount > 0 && data.atlasMaterial != null)
                                context.cmd.DrawMeshInstanced(data.mesh, 0, data.atlasMaterial, 0, data.atlasMatrices, data.atlasCount);
                        }
                        else
                        {
                            for (int i = 0; i < data.charCount; i++)
                            {
                                int count = data.counts[i];
                                if (count == 0) continue;

                                var mat = data.materials[i];
                                if (mat == null) continue;

                                context.cmd.DrawMeshInstanced(data.mesh, 0, mat, 0, data.matrices[i], count);
                            }
                        }
                    });
                }
            }
        }
    }
}
