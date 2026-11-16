using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Full-screen water pass (absorption, in-scatter, caustics, light shafts) drawn after
// opaques and sky. The shader samples the sun's shadow map, so the pass explicitly asks
// the render graph for all global textures.
public class UnderwaterVolumeFeature : ScriptableRendererFeature
{
    public Material material;

    UnderwaterVolumePass pass;

    public override void Create()
    {
        pass = new UnderwaterVolumePass
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null) return;
        var type = renderingData.cameraData.cameraType;
        if (type == CameraType.Preview || type == CameraType.Reflection) return;

        pass.material = material;
        pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        renderer.EnqueuePass(pass);
    }

    class UnderwaterVolumePass : ScriptableRenderPass
    {
        public Material material;

        class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            TextureHandle source = resources.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "_UnderwaterVolumeColor";
            desc.clearBuffer = false;
            desc.depthBufferBits = 0;
            TextureHandle destination = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Underwater Volume", out var data))
            {
                data.source = source;
                data.material = material;

                builder.UseTexture(source);
                // Shadow map, depth and normals textures are bound as globals.
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(destination, 0);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                });
            }

            // Everything after this pass (transparents, post-processing) draws on the result.
            resources.cameraColor = destination;
        }
    }
}
