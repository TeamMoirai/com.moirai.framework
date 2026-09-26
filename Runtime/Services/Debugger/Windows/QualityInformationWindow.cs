using UnityEngine;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 画质等级信息窗口（可切换等级）。
    /// <para>画质等级卡（含开关与等级按钮）构建一次常驻，仅随等级切换显式重填——轮询只重建信息清单区，避免点击落在重建边界被吞掉。</para>
    /// </summary>
    public sealed class QualityInformationWindow : ScrollableDebuggerWindowBase
    {
        #region 常量 [CONSTANTS]

        private const float REFRESH_INTERVAL = 0.25f;

        #endregion

        #region 字段 [FIELDS]

        private VisualElement _levelCard;
        private VisualElement _dynamicRoot;
        private float _countdown;
        private bool _applyExpensiveChanges;

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            BuildQualityLevelSection(root);

            _dynamicRoot = new VisualElement();
            _dynamicRoot.style.flexDirection = FlexDirection.Column;
            root.Add(_dynamicRoot);

            RefreshDynamic();
        }

        /// <inheritdoc />
        public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            // 视图尚未构建（选中后首帧 Tick 可能先于 CreateView）——跳过刷新
            if (_dynamicRoot == null)
            {
                return;
            }

            _countdown -= realElapseSeconds;
            if (_countdown > 0f)
            {
                return;
            }

            _countdown = REFRESH_INTERVAL;
            RefreshDynamic();
        }

        #endregion

        #region 分区 [SECTIONS]

        private void BuildQualityLevelSection(VisualElement root)
        {
            _levelCard = AddSection(root, "Quality Level");
            FillQualityLevelCard();
        }

        private void FillQualityLevelCard()
        {
            VisualElement card = _levelCard;
            card.Clear();

            int currentQualityLevel = QualitySettings.GetQualityLevel();
            AddRow(card, "Current Quality Level", QualitySettings.names[currentQualityLevel]);

            VisualElement toggleRow = DebuggerUI.CreateToolbarRow();
            toggleRow.Add(DebuggerUI.CreateToggle("Apply expensive changes", _applyExpensiveChanges, value => _applyExpensiveChanges = value));
            card.Add(toggleRow);

            VisualElement levelRow = DebuggerUI.CreateToolbarRow();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                int levelIndex = i;
                levelRow.Add(DebuggerUI.CreateActionButton(QualitySettings.names[i], () =>
                {
                    if (levelIndex == QualitySettings.GetQualityLevel())
                    {
                        return;
                    }

                    QualitySettings.SetQualityLevel(levelIndex, _applyExpensiveChanges);
                    FillQualityLevelCard();
                }, levelIndex == currentQualityLevel ? DebuggerUI.EButtonStyle.Active : DebuggerUI.EButtonStyle.Default));
            }

            card.Add(levelRow);
        }

        private void RefreshDynamic()
        {
            VisualElement root = _dynamicRoot;
            root.Clear();
            BuildRenderingSection(root);
            BuildShadowsSection(root);
            BuildOtherSection(root);
        }

        private static void BuildRenderingSection(VisualElement root)
        {
            VisualElement card = AddSection(root, "Rendering Information");
            AddRow(card, "Active Color Space", QualitySettings.activeColorSpace.ToString());
            AddRow(card, "Desired Color Space", QualitySettings.desiredColorSpace.ToString());
            AddRow(card, "Max Queued Frames", QualitySettings.maxQueuedFrames.ToString());
            AddRow(card, "Pixel Light Count", QualitySettings.pixelLightCount.ToString());
            AddRow(card, "Master Texture Limit", QualitySettings.globalTextureMipmapLimit.ToString());
            AddRow(card, "Anisotropic Filtering", QualitySettings.anisotropicFiltering.ToString());
            AddRow(card, "Anti Aliasing", QualitySettings.antiAliasing.ToString());
            AddRow(card, "Soft Particles", QualitySettings.softParticles.ToString());
            AddRow(card, "Soft Vegetation", QualitySettings.softVegetation.ToString());
            AddRow(card, "Realtime Reflection Probes", QualitySettings.realtimeReflectionProbes.ToString());
            AddRow(card, "Billboards Face Camera Position", QualitySettings.billboardsFaceCameraPosition.ToString());
            AddRow(card, "Resolution Scaling Fixed DPI Factor", QualitySettings.resolutionScalingFixedDPIFactor.ToString());
            AddRow(card, "Texture Streaming Enabled", QualitySettings.streamingMipmapsActive.ToString());
            AddRow(card, "Texture Streaming Add All Cameras", QualitySettings.streamingMipmapsAddAllCameras.ToString());
            AddRow(card, "Texture Streaming Memory Budget", QualitySettings.streamingMipmapsMemoryBudget.ToString());
            AddRow(card, "Texture Streaming Max Level Reduction", QualitySettings.streamingMipmapsMaxLevelReduction.ToString());
            AddRow(card, "Texture Streaming Max File IO Requests", QualitySettings.streamingMipmapsMaxFileIORequests.ToString());
            AddRow(card, "Texture Streaming Renderers Per Frame", QualitySettings.streamingMipmapsRenderersPerFrame.ToString());
        }

        private static void BuildShadowsSection(VisualElement root)
        {
            VisualElement card = AddSection(root, "Shadows Information");
            AddRow(card, "Shadowmask Mode", QualitySettings.shadowmaskMode.ToString());
            AddRow(card, "Shadow Quality", QualitySettings.shadows.ToString());
            AddRow(card, "Shadow Resolution", QualitySettings.shadowResolution.ToString());
            AddRow(card, "Shadow Projection", QualitySettings.shadowProjection.ToString());
            AddRow(card, "Shadow Distance", QualitySettings.shadowDistance.ToString());
            AddRow(card, "Shadow Near Plane Offset", QualitySettings.shadowNearPlaneOffset.ToString());
            AddRow(card, "Shadow Cascades", QualitySettings.shadowCascades.ToString());
            AddRow(card, "Shadow Cascade 2 Split", QualitySettings.shadowCascade2Split.ToString());
            AddRow(card, "Shadow Cascade 4 Split", QualitySettings.shadowCascade4Split.ToString());
        }

        private static void BuildOtherSection(VisualElement root)
        {
            VisualElement card = AddSection(root, "Other Information");
            AddRow(card, "Skin Weights", QualitySettings.skinWeights.ToString());
            AddRow(card, "VSync Count", QualitySettings.vSyncCount.ToString());
            AddRow(card, "LOD Bias", QualitySettings.lodBias.ToString());
            AddRow(card, "Maximum LOD Level", QualitySettings.maximumLODLevel.ToString());
            AddRow(card, "Particle Raycast Budget", QualitySettings.particleRaycastBudget.ToString());
            AddRow(card, "Async Upload Time Slice", StringUtility.Format("{0} ms", QualitySettings.asyncUploadTimeSlice));
            AddRow(card, "Async Upload Buffer Size", StringUtility.Format("{0} MB", QualitySettings.asyncUploadBufferSize));
            AddRow(card, "Async Upload Persistent Buffer", QualitySettings.asyncUploadPersistentBuffer.ToString());
        }

        #endregion
    }
}
