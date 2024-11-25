#if UNITY_PIPELINE_URP

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using com.zibra.liquid.Solver;

namespace com.zibra.liquid
{
    public class LiquidURPRenderComponent : ScriptableRendererFeature
    {
        [System.Serializable]
        public class LiquidURPRenderSettings
        {
            public bool IsEnabled = true;
            public RenderPassEvent InjectionPoint = RenderPassEvent.AfterRenderingTransparents;
        }

        public LiquidURPRenderSettings settings = new LiquidURPRenderSettings();

        public class CopyBackgroundURPRenderPass : ScriptableRenderPass
        {
            public ZibraLiquid liquid;
            RTHandle cameraColorTexture;

            public CopyBackgroundURPRenderPass(RenderPassEvent injectionPoint)
            {
                renderPassEvent = injectionPoint;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                cameraColorTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                Camera camera = renderingData.cameraData.camera;
                CommandBuffer cmd = CommandBufferPool.Get("ZibraLiquid.Render");

                if (liquid.cameraResources.ContainsKey(camera))
                {
                    RTHandle sourceRTHandle = RTHandles.Alloc(liquid.cameraResources[camera].background); // Convert to RTHandle
                    Blitter.BlitCameraTexture(cmd, cameraColorTexture, sourceRTHandle);
                    sourceRTHandle.Release(); // Clean up after use
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        public class LiquidNativeRenderPass : ScriptableRenderPass
        {
            public ZibraLiquid liquid;

            public LiquidNativeRenderPass(RenderPassEvent injectionPoint)
            {
                renderPassEvent = injectionPoint;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                Camera camera = renderingData.cameraData.camera;
                camera.depthTextureMode = DepthTextureMode.Depth;
                CommandBuffer cmd = CommandBufferPool.Get("ZibraLiquid.Render");

                liquid.RenderCallBack(renderingData.cameraData.camera, renderingData.cameraData.renderScale);
                ZibraLiquidBridge.SubmitInstanceEvent(cmd, liquid.CurrentInstanceID,
                                                      ZibraLiquidBridge.EventID.SetCameraParams,
                                                      liquid.camNativeParams[camera]);
                liquid.RenderLiquidNative(cmd, renderingData.cameraData.camera);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        public class LiquidURPRenderPass : ScriptableRenderPass
        {
            public ZibraLiquid liquid;

            RTHandle cameraColorTexture;
            static int upscaleColorTextureID = Shader.PropertyToID("ZibraLiquid_LiquidTempColorTexture");
            RTHandle upscaleColorTexture;

            public LiquidURPRenderPass(RenderPassEvent injectionPoint)
            {
                renderPassEvent = injectionPoint;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                cameraColorTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;
            }

            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                if (liquid.EnableDownscale)
                {
                    RenderTextureDescriptor descriptor = cameraTextureDescriptor;

                    Vector2Int dimensions = new Vector2Int(descriptor.width, descriptor.height);
                    dimensions = liquid.ApplyDownscaleFactor(dimensions);
                    descriptor.width = dimensions.x;
                    descriptor.height = dimensions.y;

                    descriptor.msaaSamples = 1;
                    descriptor.colorFormat = RenderTextureFormat.ARGBHalf;
                    descriptor.depthBufferBits = 0;

                    cmd.GetTemporaryRT(upscaleColorTextureID, descriptor, FilterMode.Bilinear);
                    upscaleColorTexture = RTHandles.Alloc(upscaleColorTextureID);
                    ConfigureTarget(upscaleColorTexture);
                    ConfigureClear(ClearFlag.All, new Color(0, 0, 0, 0));
                }
                else
                {
                    ConfigureTarget(cameraColorTexture);
                    ConfigureClear(ClearFlag.None, new Color(0, 0, 0, 0));
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                Camera camera = renderingData.cameraData.camera;
                camera.depthTextureMode = DepthTextureMode.Depth;
                CommandBuffer cmd = CommandBufferPool.Get("ZibraLiquid.Render");

                if (!liquid.EnableDownscale)
                {
                    cmd.SetRenderTarget(cameraColorTexture);
                }

                liquid.RenderLiquidMain(cmd, camera);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                if (liquid.EnableDownscale)
                {
                    cmd.ReleaseTemporaryRT(upscaleColorTextureID);
                }
            }
        }

        public class LiquidUpscaleURPRenderPass : ScriptableRenderPass
        {
            public ZibraLiquid liquid;
            static int upscaleColorTextureID = Shader.PropertyToID("ZibraLiquid_LiquidTempColorTexture");
            RTHandle upscaleColorTexture;

            public LiquidUpscaleURPRenderPass(RenderPassEvent injectionPoint)
            {
                renderPassEvent = injectionPoint;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                Camera camera = renderingData.cameraData.camera;
                camera.depthTextureMode = DepthTextureMode.Depth;
                CommandBuffer cmd = CommandBufferPool.Get("ZibraLiquid.Render");

                upscaleColorTexture = RTHandles.Alloc(upscaleColorTextureID);
                liquid.UpscaleLiquidDirect(cmd, camera, upscaleColorTexture);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        // Render passes
        public CopyBackgroundURPRenderPass[] copyPasses;
        public LiquidNativeRenderPass[] liquidNativePasses;
        public LiquidURPRenderPass[] liquidURPPasses;
        public LiquidUpscaleURPRenderPass[] upscalePasses;

        public override void Create() { }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!settings.IsEnabled) return;
            if (renderingData.cameraData.cameraType != CameraType.Game) return;

            // Processing and adding render passes logic
            // (Retain your original logic for managing passes)
        }
    }
}
#endif
