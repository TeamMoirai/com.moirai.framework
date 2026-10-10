using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 图形设备信息窗口。
    /// </summary>
    public sealed class GraphicsInformationWindow : PollingDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            VisualElement card = AddSection(root, "Graphics Information");
            AddRow(card, "Device ID", SystemInfo.graphicsDeviceID.ToString());
            AddRow(card, "Device Name", SystemInfo.graphicsDeviceName);
            AddRow(card, "Device Vendor ID", SystemInfo.graphicsDeviceVendorID.ToString());
            AddRow(card, "Device Vendor", SystemInfo.graphicsDeviceVendor);
            AddRow(card, "Device Type", SystemInfo.graphicsDeviceType.ToString());
            AddRow(card, "Device Version", SystemInfo.graphicsDeviceVersion);
            AddRow(card, "Memory Size", StringUtility.Format("{0} MB", SystemInfo.graphicsMemorySize));
            AddRow(card, "Multi Threaded", SystemInfo.graphicsMultiThreaded.ToString());
            AddRow(card, "Rendering Threading Mode", SystemInfo.renderingThreadingMode.ToString());
            AddRow(card, "HDR Display Support Flags", SystemInfo.hdrDisplaySupportFlags.ToString());
            AddRow(card, "Shader Level", StringUtility.Format("Shader Model {0}.{1}", SystemInfo.graphicsShaderLevel / 10, SystemInfo.graphicsShaderLevel % 10));
            AddRow(card, "Global Maximum LOD", Shader.globalMaximumLOD.ToString());
            AddRow(card, "Global Render Pipeline", Shader.globalRenderPipeline);
            AddRow(card, "Min OpenGLES Version", Graphics.minOpenGLESVersion.ToString());
            AddRow(card, "Active Tier", Graphics.activeTier.ToString());
            AddRow(card, "Active Color Gamut", Graphics.activeColorGamut.ToString());
            AddRow(card, "Preserve Frame Buffer Alpha", Graphics.preserveFramebufferAlpha.ToString());
            AddRow(card, "NPOT Support", SystemInfo.npotSupport.ToString());
            AddRow(card, "Max Texture Size", SystemInfo.maxTextureSize.ToString());
            AddRow(card, "Supported Render Target Count", SystemInfo.supportedRenderTargetCount.ToString());
            AddRow(card, "Supported Random Write Target Count", SystemInfo.supportedRandomWriteTargetCount.ToString());
            AddRow(card, "Copy Texture Support", SystemInfo.copyTextureSupport.ToString());
            AddRow(card, "Uses Reversed ZBuffer", SystemInfo.usesReversedZBuffer.ToString());
            AddRow(card, "Max Cubemap Size", SystemInfo.maxCubemapSize.ToString());
            AddRow(card, "Graphics UV Starts At Top", SystemInfo.graphicsUVStartsAtTop.ToString());
            AddRow(card, "Constant Buffer Offset Alignment", SystemInfo.constantBufferOffsetAlignment.ToString());
            AddRow(card, "Has Hidden Surface Removal On GPU", SystemInfo.hasHiddenSurfaceRemovalOnGPU.ToString());
            AddRow(card, "Has Dynamic Uniform Array Indexing In Fragment Shaders", SystemInfo.hasDynamicUniformArrayIndexingInFragmentShaders.ToString());
            AddRow(card, "Has Mip Max Level", SystemInfo.hasMipMaxLevel.ToString());
            AddRow(card, "Uses Load Store Actions", SystemInfo.usesLoadStoreActions.ToString());
            AddRow(card, "Max Compute Buffer Inputs Compute", SystemInfo.maxComputeBufferInputsCompute.ToString());
            AddRow(card, "Max Compute Buffer Inputs Domain", SystemInfo.maxComputeBufferInputsDomain.ToString());
            AddRow(card, "Max Compute Buffer Inputs Fragment", SystemInfo.maxComputeBufferInputsFragment.ToString());
            AddRow(card, "Max Compute Buffer Inputs Geometry", SystemInfo.maxComputeBufferInputsGeometry.ToString());
            AddRow(card, "Max Compute Buffer Inputs Hull", SystemInfo.maxComputeBufferInputsHull.ToString());
            AddRow(card, "Max Compute Buffer Inputs Vertex", SystemInfo.maxComputeBufferInputsVertex.ToString());
            AddRow(card, "Max Compute Work Group Size", SystemInfo.maxComputeWorkGroupSize.ToString());
            AddRow(card, "Max Compute Work Group Size X", SystemInfo.maxComputeWorkGroupSizeX.ToString());
            AddRow(card, "Max Compute Work Group Size Y", SystemInfo.maxComputeWorkGroupSizeY.ToString());
            AddRow(card, "Max Compute Work Group Size Z", SystemInfo.maxComputeWorkGroupSizeZ.ToString());
            AddRow(card, "Supports Sparse Textures", SystemInfo.supportsSparseTextures.ToString());
            AddRow(card, "Supports 3D Textures", SystemInfo.supports3DTextures.ToString());
            AddRow(card, "Supports Shadows", SystemInfo.supportsShadows.ToString());
            AddRow(card, "Supports Raw Shadow Depth Sampling", SystemInfo.supportsRawShadowDepthSampling.ToString());
            AddRow(card, "Supports Compute Shader", SystemInfo.supportsComputeShaders.ToString());
            AddRow(card, "Supports Instancing", SystemInfo.supportsInstancing.ToString());
            AddRow(card, "Supports 2D Array Textures", SystemInfo.supports2DArrayTextures.ToString());
            AddRow(card, "Supports Motion Vectors", SystemInfo.supportsMotionVectors.ToString());
            AddRow(card, "Supports Cubemap Array Textures", SystemInfo.supportsCubemapArrayTextures.ToString());
            AddRow(card, "Supports 3D Render Textures", SystemInfo.supports3DRenderTextures.ToString());
            AddRow(card, "Supports Texture Wrap Mirror Once", SystemInfo.supportsTextureWrapMirrorOnce.ToString());
            AddRow(card, "Supports Graphics Fence", SystemInfo.supportsGraphicsFence.ToString());
            AddRow(card, "Supports Async Compute", SystemInfo.supportsAsyncCompute.ToString());
            AddRow(card, "Supports Multi-sampled Textures", SystemInfo.supportsMultisampledTextures.ToString());
            AddRow(card, "Supports Async GPU Readback", SystemInfo.supportsAsyncGPUReadback.ToString());
            AddRow(card, "Supports 32bits Index Buffer", SystemInfo.supports32bitsIndexBuffer.ToString());
            AddRow(card, "Supports Hardware Quad Topology", SystemInfo.supportsHardwareQuadTopology.ToString());
            AddRow(card, "Supports Mip Streaming", SystemInfo.supportsMipStreaming.ToString());
            AddRow(card, "Supports Multi-sample Auto Resolve", SystemInfo.supportsMultisampleAutoResolve.ToString());
            AddRow(card, "Supports Separated Render Targets Blend", SystemInfo.supportsSeparatedRenderTargetsBlend.ToString());
            AddRow(card, "Supports Set Constant Buffer", SystemInfo.supportsSetConstantBuffer.ToString());
            AddRow(card, "Supports Geometry Shaders", SystemInfo.supportsGeometryShaders.ToString());
            AddRow(card, "Supports Ray Tracing", SystemInfo.supportsRayTracing.ToString());
            AddRow(card, "Supports Tessellation Shaders", SystemInfo.supportsTessellationShaders.ToString());
            AddRow(card, "Supports Compressed 3D Textures", SystemInfo.supportsCompressed3DTextures.ToString());
            AddRow(card, "Supports Conservative Raster", SystemInfo.supportsConservativeRaster.ToString());
            AddRow(card, "Supports GPU Recorder", SystemInfo.supportsGpuRecorder.ToString());
            AddRow(card, "Supports Multi-sampled 2D Array Textures", SystemInfo.supportsMultisampled2DArrayTextures.ToString());
            AddRow(card, "Supports Multiview", SystemInfo.supportsMultiview.ToString());
            AddRow(card, "Supports Render Target Array Index From Vertex Shader", SystemInfo.supportsRenderTargetArrayIndexFromVertexShader.ToString());
        }

        #endregion
    }
}
