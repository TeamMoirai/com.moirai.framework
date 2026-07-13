using System;
using System.Collections;
using System.Collections.Generic;
using Moirai.Atropos;
using UnityEngine;
using UnityEngine.UI;

namespace Utility
{
    /// <summary>
    /// TweenUtility 全功能运行时测试。挂载到任意 GameObject 即可运行。
    /// 在 Scene 中创建测试用物体，依次执行所有功能测试，输出日志与 GUI 结果。
    /// </summary>
    public class TweenUtilityTest : MonoBehaviour
    {
        private readonly List<TestResult> _results = new();
        private bool _running;
        private Vector2 _scrollPos;

        // Custom 实时值追踪
        private float _customFloatValue;
        private int _customIntValue;
        private long _customLongValue;
        private Vector3 _customVec3Value;
        private int _activeCustomType; // 0=none, 1=float, 2=int, 3=long, 4=Vec3

        private struct TestResult
        {
            public string Name;
            public bool Passed;
            public string Message;
        }

        private void Start()
        {
            StartCoroutine(RunAllTests());
        }

        private IEnumerator RunAllTests()
        {
            _running = true;
            Debug.Log("=== TweenUtility 全功能测试开始 ===");

            yield return Test_Delay();
            yield return Test_Position();
            yield return Test_PositionXYZ();
            yield return Test_LocalPosition();
            yield return Test_LocalPositionXYZ();
            yield return Test_RotationVec3();
            yield return Test_LocalRotationVec3();
            yield return Test_RotationQuat();
            yield return Test_LocalRotationQuat();
            yield return Test_ScaleFloat();
            yield return Test_ScaleVec3();
            yield return Test_ScaleXYZ();
            yield return Test_SpriteColor();
            yield return Test_SpriteAlpha();
            yield return Test_MaterialColor();
            yield return Test_UISliderValue();
            yield return Test_UIAnchoredPosition();
            yield return Test_UIAnchoredPositionXY();
            yield return Test_UIAnchoredPosition3D();
            yield return Test_UISizeDelta();
            yield return Test_UIColor();
            yield return Test_UICanvasGroupAlpha();
            yield return Test_UIGraphicAlpha();
            yield return Test_UIFillAmount();
            yield return Test_UINormalizedPosition();
            yield return Test_UIHNormalizedPosition();
            yield return Test_UIVNormalizedPosition();
            yield return Test_CustomFloat();
            yield return Test_CustomInt();
            yield return Test_CustomLong();
            yield return Test_CustomVector3();
            _activeCustomType = 0;
            yield return Test_IsTweening();
            yield return Test_GetTweenCount();
            yield return Test_IsAlive();
            yield return Test_Stop();
            yield return Test_Complete();
            yield return Test_StopAll();
            yield return Test_CompleteAll();
            yield return Test_CycleRestart();
            yield return Test_CycleYoyo();
            yield return Test_CycleIncremental();
            yield return Test_TargetDestroy();
            yield return Test_MultipleEases();

            _running = false;
            int passed = 0, failed = 0;
            foreach (var r in _results)
            {
                if (r.Passed) passed++;
                else failed++;
            }
            Debug.Log($"=== 测试完成: {passed} 通过, {failed} 失败, 共 {_results.Count} 项 ===");
        }

        #region 辅助方法

        private GameObject CreateCube(string name, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            return go;
        }

        private void Assert(string testName, bool condition, string msg = "")
        {
            _results.Add(new TestResult { Name = testName, Passed = condition, Message = msg });
            string status = condition ? "PASS" : "FAIL";
            string log = $"[{status}] {testName}";
            if (!string.IsNullOrEmpty(msg)) log += $" — {msg}";
            if (condition) Debug.Log(log);
            else Debug.LogError(log);
        }

        private IEnumerator Wait(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private float Approx(float a, float b, float tolerance = 0.05f)
        {
            return Mathf.Abs(a - b);
        }

        private bool ApproxEq(float a, float b, float tolerance = 0.05f)
        {
            return Mathf.Abs(a - b) <= tolerance;
        }

        #endregion

        #region Delay

        private IEnumerator Test_Delay()
        {
            float duration = 0.3f;
            bool completed = false;
            float startTime = Time.time;

            TweenUtility.Delay(duration, () => completed = true);

            yield return Wait(duration + 0.1f);

            float elapsed = Time.time - startTime;
            Assert("Delay", completed && elapsed >= duration - 0.05f,
                $"completed={completed}, elapsed={elapsed:F3}");
        }

        #endregion

        #region Position

        private IEnumerator Test_Position()
        {
            var go = CreateCube("Test_Position", Vector3.zero);
            Vector3 target = new Vector3(5, 0, 0);

            TweenUtility.Position(go.transform, target, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(go.transform.position.x, 5f);
            Assert("Position(Vector3)", ok, $"x={go.transform.position.x:F3}");
            Destroy(go);
        }

        private IEnumerator Test_PositionXYZ()
        {
            var go = CreateCube("Test_PositionXYZ", Vector3.zero);
            float startX = 0, endX = 3;
            float startY = 0, endY = 4;
            float startZ = 0, endZ = 5;

            TweenUtility.PositionX(go.transform, endX, 0.3f);
            TweenUtility.PositionY(go.transform, endY, 0.3f);
            TweenUtility.PositionZ(go.transform, endZ, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(go.transform.position.x, endX)
                      && ApproxEq(go.transform.position.y, endY)
                      && ApproxEq(go.transform.position.z, endZ);
            Assert("PositionX/Y/Z", ok,
                $"pos={go.transform.position}");
            Destroy(go);
        }

        #endregion

        #region LocalPosition

        private IEnumerator Test_LocalPosition()
        {
            var go = CreateCube("Test_LocalPos", Vector3.one * 10);
            Vector3 target = new Vector3(15, 10, 10);

            TweenUtility.LocalPosition(go.transform, target, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(go.transform.localPosition.x, 15f);
            Assert("LocalPosition(Vector3)", ok, $"localPos={go.transform.localPosition}");
            Destroy(go);
        }

        private IEnumerator Test_LocalPositionXYZ()
        {
            var go = CreateCube("Test_LocalPosXYZ", Vector3.one * 10);

            TweenUtility.LocalPositionX(go.transform, 20f, 0.3f);
            TweenUtility.LocalPositionY(go.transform, 20f, 0.3f);
            TweenUtility.LocalPositionZ(go.transform, 20f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(go.transform.localPosition.x, 20f)
                      && ApproxEq(go.transform.localPosition.y, 20f)
                      && ApproxEq(go.transform.localPosition.z, 20f);
            Assert("LocalPositionX/Y/Z", ok, $"localPos={go.transform.localPosition}");
            Destroy(go);
        }

        #endregion

        #region Rotation

        private IEnumerator Test_RotationVec3()
        {
            var go = CreateCube("Test_RotVec3", Vector3.zero);
            Vector3 target = new Vector3(0, 90, 0);

            TweenUtility.Rotation(go.transform, target, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(go.transform.eulerAngles.y, 90f);
            Assert("Rotation(Vector3)", ok, $"euler={go.transform.eulerAngles}");
            Destroy(go);
        }

        private IEnumerator Test_LocalRotationVec3()
        {
            var go = CreateCube("Test_LocalRotVec3", Vector3.zero);
            Vector3 target = new Vector3(0, 180, 0);

            TweenUtility.LocalRotation(go.transform, target, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(go.transform.localEulerAngles.y, 180f, 1f);
            Assert("LocalRotation(Vector3)", ok, $"localEuler={go.transform.localEulerAngles}");
            Destroy(go);
        }

        private IEnumerator Test_RotationQuat()
        {
            var go = CreateCube("Test_RotQuat", Vector3.zero);
            Quaternion target = Quaternion.Euler(45, 45, 0);

            TweenUtility.Rotation(go.transform, target, 0.3f);
            yield return Wait(0.35f);

            float angle = Quaternion.Angle(go.transform.rotation, target);
            bool ok = angle < 5f;
            Assert("Rotation(Quaternion)", ok, $"angleDiff={angle:F2}");
            Destroy(go);
        }

        private IEnumerator Test_LocalRotationQuat()
        {
            var go = CreateCube("Test_LocalRotQuat", Vector3.zero);
            Quaternion target = Quaternion.Euler(0, 0, 90);

            TweenUtility.LocalRotation(go.transform, target, 0.3f);
            yield return Wait(0.35f);

            float angle = Quaternion.Angle(go.transform.localRotation, target);
            bool ok = angle < 5f;
            Assert("LocalRotation(Quaternion)", ok, $"angleDiff={angle:F2}");
            Destroy(go);
        }

        #endregion

        #region Scale

        private IEnumerator Test_ScaleFloat()
        {
            var go = CreateCube("Test_ScaleFloat", Vector3.zero);

            TweenUtility.Scale(go.transform, 2f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(go.transform.localScale.x, 2f)
                      && ApproxEq(go.transform.localScale.y, 2f)
                      && ApproxEq(go.transform.localScale.z, 2f);
            Assert("Scale(float)", ok, $"scale={go.transform.localScale}");
            Destroy(go);
        }

        private IEnumerator Test_ScaleVec3()
        {
            var go = CreateCube("Test_ScaleVec3", Vector3.zero);
            Vector3 target = new Vector3(1, 2, 3);

            TweenUtility.Scale(go.transform, target, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(go.transform.localScale.x, 1f)
                      && ApproxEq(go.transform.localScale.y, 2f)
                      && ApproxEq(go.transform.localScale.z, 3f);
            Assert("Scale(Vector3)", ok, $"scale={go.transform.localScale}");
            Destroy(go);
        }

        private IEnumerator Test_ScaleXYZ()
        {
            var go = CreateCube("Test_ScaleXYZ", Vector3.zero);

            TweenUtility.ScaleX(go.transform, 1.5f, 0.3f);
            TweenUtility.ScaleY(go.transform, 2.5f, 0.3f);
            TweenUtility.ScaleZ(go.transform, 3.5f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(go.transform.localScale.x, 1.5f)
                      && ApproxEq(go.transform.localScale.y, 2.5f)
                      && ApproxEq(go.transform.localScale.z, 3.5f);
            Assert("ScaleX/Y/Z", ok, $"scale={go.transform.localScale}");
            Destroy(go);
        }

        #endregion

        #region SpriteRenderer

        private IEnumerator Test_SpriteColor()
        {
            // SpriteRenderer 不能加到有 MeshFilter 的 Cube 上，需单独创建空物体
            var go = new GameObject("Test_SpriteColor");
            go.transform.position = Vector3.zero;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.color = Color.white;

            TweenUtility.Color(sr, Color.red, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(sr.color.r, 1f) && ApproxEq(sr.color.g, 0f) && ApproxEq(sr.color.b, 0f);
            Assert("SpriteRenderer.Color", ok, $"color={sr.color}");
            Destroy(go);
        }

        private IEnumerator Test_SpriteAlpha()
        {
            var go = new GameObject("Test_SpriteAlpha");
            go.transform.position = Vector3.zero;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.color = Color.white;

            TweenUtility.Alpha(sr, 0f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(sr.color.a, 0f);
            Assert("SpriteRenderer.Alpha", ok, $"alpha={sr.color.a:F3}");
            Destroy(go);
        }

        private IEnumerator Test_MaterialColor()
        {
            var go = CreateCube("Test_MatColor", Vector3.zero);
            var mr = go.GetComponent<MeshRenderer>();
            var mat = mr.material;
            mat.color = Color.white;

            TweenUtility.MaterialColor(mat, Color.white, Color.blue, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(mat.color.r, 0f) && ApproxEq(mat.color.g, 0f);
            Assert("MaterialColor", ok, $"color={mat.color}");
            Destroy(go);
        }

        #endregion

        #region UI

        private Canvas CreateUICanvas(string name)
        {
            var canvasGo = new GameObject(name);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>
        /// 创建带 content 和 viewport 的 ScrollRect（normalizedPosition 需要这些才能正常工作）。
        /// </summary>
        private (ScrollRect sr, GameObject canvasGo) CreateScrollRect(string name)
        {
            var canvas = CreateUICanvas(name);

            // ScrollRect 本体
            var srGo = new GameObject("ScrollRect");
            srGo.transform.SetParent(canvas.transform, false);
            var srRt = srGo.AddComponent<RectTransform>();
            srRt.sizeDelta = new Vector2(200, 200);
            var sr = srGo.AddComponent<ScrollRect>();

            // Viewport
            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(srGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.sizeDelta = new Vector2(200, 200);
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0.1f);
            var mask = vpGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            sr.viewport = vpRt;

            // Content
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(vpGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.sizeDelta = new Vector2(400, 400); // 大于 viewport 以产生滚动
            sr.content = contentRt;

            return (sr, canvas.gameObject);
        }

        private IEnumerator Test_UISliderValue()
        {
            var canvas = CreateUICanvas("TestCanvas_Slider");
            var sliderGo = new GameObject("Slider");
            sliderGo.transform.SetParent(canvas.transform, false);
            var slider = sliderGo.AddComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 0;

            TweenUtility.UISliderValue(slider, 0.8f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(slider.value, 0.8f, 0.05f);
            Assert("UISliderValue", ok, $"value={slider.value:F3}");
            Destroy(canvas.gameObject);
        }

        private IEnumerator Test_UIAnchoredPosition()
        {
            var canvas = CreateUICanvas("TestCanvas_AnchPos");
            var rtGo = new GameObject("RT");
            rtGo.transform.SetParent(canvas.transform, false);
            var rt = rtGo.AddComponent<RectTransform>();
            rt.anchoredPosition = Vector2.zero;

            TweenUtility.UIAnchoredPosition(rt, new Vector2(100, 200), 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(rt.anchoredPosition.x, 100f, 1f)
                      && ApproxEq(rt.anchoredPosition.y, 200f, 1f);
            Assert("UIAnchoredPosition", ok, $"pos={rt.anchoredPosition}");
            Destroy(canvas.gameObject);
        }

        private IEnumerator Test_UIAnchoredPositionXY()
        {
            var canvas = CreateUICanvas("TestCanvas_AnchXY");
            var rtGo = new GameObject("RT");
            rtGo.transform.SetParent(canvas.transform, false);
            var rt = rtGo.AddComponent<RectTransform>();
            rt.anchoredPosition = Vector2.zero;

            TweenUtility.UIAnchoredPositionX(rt, 150f, 0.3f);
            TweenUtility.UIAnchoredPositionY(rt, 250f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(rt.anchoredPosition.x, 150f, 1f)
                      && ApproxEq(rt.anchoredPosition.y, 250f, 1f);
            Assert("UIAnchoredPositionX/Y", ok, $"pos={rt.anchoredPosition}");
            Destroy(canvas.gameObject);
        }

        private IEnumerator Test_UIAnchoredPosition3D()
        {
            var canvas = CreateUICanvas("TestCanvas_Anch3D");
            var rtGo = new GameObject("RT");
            rtGo.transform.SetParent(canvas.transform, false);
            var rt = rtGo.AddComponent<RectTransform>();
            rt.anchoredPosition3D = Vector3.zero;

            TweenUtility.UIAnchoredPosition3D(rt, new Vector3(10, 20, 30), 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(rt.anchoredPosition3D.x, 10f, 1f)
                      && ApproxEq(rt.anchoredPosition3D.y, 20f, 1f)
                      && ApproxEq(rt.anchoredPosition3D.z, 30f, 1f);
            Assert("UIAnchoredPosition3D", ok, $"pos3d={rt.anchoredPosition3D}");
            Destroy(canvas.gameObject);
        }

        private IEnumerator Test_UISizeDelta()
        {
            var canvas = CreateUICanvas("TestCanvas_SizeDelta");
            var rtGo = new GameObject("RT");
            rtGo.transform.SetParent(canvas.transform, false);
            var rt = rtGo.AddComponent<RectTransform>();
            rt.sizeDelta = Vector2.zero;

            TweenUtility.UISizeDelta(rt, new Vector2(300, 400), 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(rt.sizeDelta.x, 300f, 1f)
                      && ApproxEq(rt.sizeDelta.y, 400f, 1f);
            Assert("UISizeDelta", ok, $"size={rt.sizeDelta}");
            Destroy(canvas.gameObject);
        }

        private IEnumerator Test_UIColor()
        {
            var canvas = CreateUICanvas("TestCanvas_Color");
            var imgGo = new GameObject("Image");
            imgGo.transform.SetParent(canvas.transform, false);
            imgGo.AddComponent<RectTransform>();
            var img = imgGo.AddComponent<Image>();
            img.color = Color.white;

            TweenUtility.Color(img, Color.green, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(img.color.g, 1f) && ApproxEq(img.color.r, 0f);
            Assert("UIColor(Graphic)", ok, $"color={img.color}");
            Destroy(canvas.gameObject);
        }

        private IEnumerator Test_UICanvasGroupAlpha()
        {
            var canvas = CreateUICanvas("TestCanvas_CGAlpha");
            var cgGo = new GameObject("CanvasGroup");
            cgGo.transform.SetParent(canvas.transform, false);
            cgGo.AddComponent<RectTransform>();
            var cg = cgGo.AddComponent<CanvasGroup>();
            cg.alpha = 1f;

            TweenUtility.Alpha(cg, 0f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(cg.alpha, 0f, 0.05f);
            Assert("UICanvasGroupAlpha", ok, $"alpha={cg.alpha:F3}");
            Destroy(canvas.gameObject);
        }

        private IEnumerator Test_UIGraphicAlpha()
        {
            var canvas = CreateUICanvas("TestCanvas_GfxAlpha");
            var imgGo = new GameObject("Image");
            imgGo.transform.SetParent(canvas.transform, false);
            imgGo.AddComponent<RectTransform>();
            var img = imgGo.AddComponent<Image>();
            img.color = new Color(1, 1, 1, 1);

            TweenUtility.Alpha(img, 0f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(img.color.a, 0f, 0.05f);
            Assert("UIGraphicAlpha", ok, $"alpha={img.color.a:F3}");
            Destroy(canvas.gameObject);
        }

        private IEnumerator Test_UIFillAmount()
        {
            var canvas = CreateUICanvas("TestCanvas_Fill");
            var imgGo = new GameObject("Image");
            imgGo.transform.SetParent(canvas.transform, false);
            imgGo.AddComponent<RectTransform>();
            var img = imgGo.AddComponent<Image>();
            img.type = Image.Type.Filled;
            img.fillAmount = 0f;

            TweenUtility.UIFillAmount(img, 1f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(img.fillAmount, 1f, 0.05f);
            Assert("UIFillAmount", ok, $"fill={img.fillAmount:F3}");
            Destroy(canvas.gameObject);
        }

        private IEnumerator Test_UINormalizedPosition()
        {
            var (sr, canvasGo) = CreateScrollRect("TestCanvas_NormPos");
            sr.normalizedPosition = Vector2.zero;

            TweenUtility.UINormalizedPosition(sr, new Vector2(1, 1), 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(sr.normalizedPosition.x, 1f, 0.1f)
                      && ApproxEq(sr.normalizedPosition.y, 1f, 0.1f);
            Assert("UINormalizedPosition", ok, $"normPos={sr.normalizedPosition}");
            Destroy(canvasGo);
        }

        private IEnumerator Test_UIHNormalizedPosition()
        {
            var (sr, canvasGo) = CreateScrollRect("TestCanvas_HNormPos");
            sr.horizontalNormalizedPosition = 0f;

            TweenUtility.UIHorizontalNormalizedPosition(sr, 1f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(sr.horizontalNormalizedPosition, 1f, 0.1f);
            Assert("UIHNormalizedPosition", ok, $"hNormPos={sr.horizontalNormalizedPosition:F3}");
            Destroy(canvasGo);
        }

        private IEnumerator Test_UIVNormalizedPosition()
        {
            var (sr, canvasGo) = CreateScrollRect("TestCanvas_VNormPos");
            sr.verticalNormalizedPosition = 0f;

            TweenUtility.UIVerticalNormalizedPosition(sr, 1f, 0.3f);
            yield return Wait(0.35f);

            bool ok = ApproxEq(sr.verticalNormalizedPosition, 1f, 0.1f);
            Assert("UIVNormalizedPosition", ok, $"vNormPos={sr.verticalNormalizedPosition:F3}");
            Destroy(canvasGo);
        }

        #endregion

        #region Custom

        private IEnumerator Test_CustomFloat()
        {
            float captured = -1f;
            var go = CreateCube("Test_CustomFloat", Vector3.zero);
            _activeCustomType = 1;

            TweenUtility.Custom(go, 0f, 10f, 0.3f, (t, v) =>
            {
                captured = v;
                _customFloatValue = v;
            });
            yield return Wait(0.35f);

            bool ok = ApproxEq(captured, 10f, 0.1f);
            Assert("Custom<float>", ok, $"value={captured:F3}");
            Destroy(go);
        }

        private IEnumerator Test_CustomInt()
        {
            int captured = -1;
            var go = CreateCube("Test_CustomInt", Vector3.zero);
            _activeCustomType = 2;

            TweenUtility.Custom(go, 0, 100, 0.3f, (t, v) =>
            {
                captured = v;
                _customIntValue = v;
            });
            yield return Wait(0.35f);

            bool ok = captured == 100;
            Assert("Custom<int>", ok, $"value={captured}");
            Destroy(go);
        }

        private IEnumerator Test_CustomLong()
        {
            long captured = -1;
            var go = CreateCube("Test_CustomLong", Vector3.zero);
            _activeCustomType = 3;

            TweenUtility.Custom(go, 0L, 1000L, 0.3f, (t, v) =>
            {
                captured = v;
                _customLongValue = v;
            });
            yield return Wait(0.35f);

            bool ok = captured == 1000;
            Assert("Custom<long>", ok, $"value={captured}");
            Destroy(go);
        }

        private IEnumerator Test_CustomVector3()
        {
            Vector3 captured = Vector3.negativeInfinity;
            var go = CreateCube("Test_CustomVec3", Vector3.zero);
            _activeCustomType = 4;

            TweenUtility.Custom(go, Vector3.zero, new Vector3(1, 2, 3), 0.3f,
                (t, v) =>
                {
                    captured = v;
                    _customVec3Value = v;
                });
            yield return Wait(0.35f);

            bool ok = ApproxEq(captured.x, 1f) && ApproxEq(captured.y, 2f) && ApproxEq(captured.z, 3f);
            Assert("Custom<Vector3>", ok, $"value={captured}");
            Destroy(go);
        }

        #endregion

        #region 查询与控制

        private IEnumerator Test_IsTweening()
        {
            var go = CreateCube("Test_IsTweening", Vector3.zero);

            TweenUtility.Position(go.transform, Vector3.one, 0.5f);
            yield return null; // 等一帧让 tween 注册

            bool tweening = TweenUtility.IsTweening(go.transform);
            Assert("IsTweening", tweening, $"result={tweening}");
            Destroy(go);
            yield return Wait(0.6f);
        }

        private IEnumerator Test_GetTweenCount()
        {
            var go = CreateCube("Test_GetTweenCount", Vector3.zero);

            TweenUtility.Position(go.transform, Vector3.one, 0.5f);
            TweenUtility.Scale(go.transform, 2f, 0.5f);
            yield return null;

            int count = TweenUtility.GetTweenCount(go.transform);
            Assert("GetTweenCount", count == 2, $"count={count}");
            Destroy(go);
            yield return Wait(0.6f);
        }

        private IEnumerator Test_IsAlive()
        {
            var go = CreateCube("Test_IsAlive", Vector3.zero);

            long id = TweenUtility.Position(go.transform, Vector3.one, 0.5f);
            yield return null;

            bool alive = TweenUtility.IsAlive(id);
            Assert("IsAlive (before complete)", alive, $"alive={alive}");
            Destroy(go);
            yield return Wait(0.6f);
        }

        private IEnumerator Test_Stop()
        {
            var go = CreateCube("Test_Stop", Vector3.zero);

            long id = TweenUtility.Position(go.transform, new Vector3(100, 0, 0), 1f);
            yield return Wait(0.1f);

            TweenUtility.Stop(id);
            float xBefore = go.transform.position.x;
            yield return Wait(0.5f);
            float xAfter = go.transform.position.x;

            // 停止后不应再变化
            bool ok = ApproxEq(xBefore, xAfter, 0.1f) && !ApproxEq(xAfter, 100f);
            Assert("Stop", ok, $"before={xBefore:F3}, after={xAfter:F3}");
            Destroy(go);
        }

        private IEnumerator Test_Complete()
        {
            var go = CreateCube("Test_Complete", Vector3.zero);

            long id = TweenUtility.Position(go.transform, new Vector3(10, 0, 0), 1f);
            yield return null;

            TweenUtility.Complete(id);
            bool ok = ApproxEq(go.transform.position.x, 10f, 0.1f);
            Assert("Complete", ok, $"x={go.transform.position.x:F3}");
            Destroy(go);
            yield return Wait(0.2f);
        }

        private IEnumerator Test_StopAll()
        {
            var go = CreateCube("Test_StopAll", Vector3.zero);

            TweenUtility.Position(go.transform, Vector3.one, 0.5f);
            TweenUtility.Scale(go.transform, 5f, 0.5f);
            yield return null;

            int stopped = TweenUtility.StopAll(go.transform);
            Assert("StopAll", stopped == 2, $"stopped={stopped}");
            Destroy(go);
            yield return Wait(0.6f);
        }

        private IEnumerator Test_CompleteAll()
        {
            var go = CreateCube("Test_CompleteAll", Vector3.zero);

            TweenUtility.Position(go.transform, new Vector3(7, 0, 0), 1f);
            TweenUtility.Scale(go.transform, 3f, 1f);
            yield return null;

            int completed = TweenUtility.CompleteAll(go.transform);
            bool posOk = ApproxEq(go.transform.position.x, 7f, 0.1f);
            bool scaleOk = ApproxEq(go.transform.localScale.x, 3f, 0.1f);
            Assert("CompleteAll", completed == 2 && posOk && scaleOk,
                $"completed={completed}, pos={go.transform.position.x:F3}, scale={go.transform.localScale.x:F3}");
            Destroy(go);
        }

        #endregion

        #region 循环模式

        private IEnumerator Test_CycleRestart()
        {
            var go = CreateCube("Test_CycleRestart", Vector3.zero);

            // 2 次循环，每次 0.15s，总共 0.3s
            TweenUtility.PositionX(go.transform, 5f, 0.15f,
                TweenUtility.EEase.Linear, 2, TweenUtility.ECycleMode.Restart);
            yield return Wait(0.35f);

            // Restart 模式下，最终位置应接近 5
            bool ok = ApproxEq(go.transform.position.x, 5f, 0.5f);
            Assert("CycleRestart", ok, $"x={go.transform.position.x:F3}");
            Destroy(go);
        }

        private IEnumerator Test_CycleYoyo()
        {
            var go = CreateCube("Test_CycleYoyo", Vector3.zero);

            // 2 次循环，Yoyo，每次 0.15s
            TweenUtility.PositionX(go.transform, 5f, 0.15f,
                TweenUtility.EEase.Linear, 2, TweenUtility.ECycleMode.Yoyo);
            yield return Wait(0.35f);

            // Yoyo: 0→5→0，最终应回到 0
            bool ok = ApproxEq(go.transform.position.x, 0f, 0.5f);
            Assert("CycleYoyo", ok, $"x={go.transform.position.x:F3}");
            Destroy(go);
        }

        private IEnumerator Test_CycleIncremental()
        {
            var go = CreateCube("Test_CycleIncremental", Vector3.zero);

            // 3 次 Incremental，每次 0.1s
            TweenUtility.PositionX(go.transform, 5f, 0.1f,
                TweenUtility.EEase.Linear, 3, TweenUtility.ECycleMode.Incremental);
            yield return Wait(0.4f);

            // Incremental: 0→5, 5→10, 10→15
            bool ok = ApproxEq(go.transform.position.x, 15f, 1f);
            Assert("CycleIncremental", ok, $"x={go.transform.position.x:F3}");
            Destroy(go);
        }

        #endregion

        #region 目标销毁安全

        private IEnumerator Test_TargetDestroy()
        {
            var go = CreateCube("Test_TargetDestroy", Vector3.zero);

            // 启动一个长 tween
            TweenUtility.Position(go.transform, new Vector3(100, 0, 0), 5f);
            yield return null;

            bool wasTweening = TweenUtility.IsTweening(go.transform);

            // 销毁目标
            Destroy(go);
            yield return Wait(0.2f);

            // 不应报错，IsTweening 应返回 false（因为已无活跃 tween 绑定该对象）
            bool afterDestroy = TweenUtility.IsTweening(go);
            Assert("TargetDestroy_Safe", !afterDestroy,
                $"beforeDestroy={wasTweening}, afterDestroy={afterDestroy}");
        }

        #endregion

        #region 多缓动类型

        private IEnumerator Test_MultipleEases()
        {
            var go = CreateCube("Test_MultiEases", Vector3.zero);
            bool allOk = true;

            var easeTypes = new[]
            {
                TweenUtility.EEase.Linear,
                TweenUtility.EEase.InQuad,
                TweenUtility.EEase.OutQuad,
                TweenUtility.EEase.InOutCubic,
                TweenUtility.EEase.InElastic,
                TweenUtility.EEase.OutBounce,
                TweenUtility.EEase.InBack,
                TweenUtility.EEase.InExpo,
                TweenUtility.EEase.InCirc,
            };

            foreach (var ease in easeTypes)
            {
                go.transform.position = Vector3.zero;
                TweenUtility.PositionX(go.transform, 10f, 0.2f, ease);
                yield return Wait(0.25f);

                bool reached = go.transform.position.x > 9f;
                if (!reached)
                {
                    allOk = false;
                    Debug.LogError($"[FAIL] Ease {ease} did not reach target: x={go.transform.position.x:F3}");
                }
            }

            Assert("MultipleEases", allOk, $"tested {easeTypes.Length} ease types");
            Destroy(go);
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            if (_results.Count == 0 && !_running) return;

            GUILayout.BeginArea(new Rect(10, 10, 500, Screen.height - 20));
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            GUILayout.Label($"TweenUtility 测试结果 ({_results.Count} 项)", new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold });

            int passed = 0, failed = 0;
            foreach (var r in _results)
            {
                if (r.Passed) passed++;
                else failed++;

                var style = new GUIStyle(GUI.skin.label)
                {
                    richText = true
                };
                string color = r.Passed ? "#4CAF50" : "#F44336";
                string text = $"<color={color}>[{(r.Passed ? "PASS" : "FAIL")}]</color> {r.Name}";
                if (!string.IsNullOrEmpty(r.Message))
                    text += $" — {r.Message}";
                GUILayout.Label(text, style);
            }

            if (!_running)
            {
                string summary = failed == 0
                    ? $"<color=#4CAF50>全部通过: {passed}/{_results.Count}</color>"
                    : $"<color=#F44336>{failed} 失败</color> / <color=#4CAF50>{passed} 通过</color> / 共 {_results.Count}";
                GUILayout.Label(summary, new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold });
            }
            else
            {
                GUILayout.Label("运行中...", new GUIStyle(GUI.skin.label) { fontSize = 14 });
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Custom 实时值面板 — 屏幕居中，仅显示当前类型
            if (_activeCustomType > 0 && _running)
            {
                float panelW = 320f;
                float panelH = 80f;
                float x = (Screen.width - panelW) * 0.5f;
                float y = (Screen.height - panelH) * 0.5f;
                GUILayout.BeginArea(new Rect(x, y, panelW, panelH), GUI.skin.box);
                var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                var valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };

                switch (_activeCustomType)
                {
                    case 1:
                        GUILayout.Label("Custom<float>", titleStyle);
                        GUILayout.Label($"{_customFloatValue:F4}", valueStyle);
                        break;
                    case 2:
                        GUILayout.Label("Custom<int>", titleStyle);
                        GUILayout.Label($"{_customIntValue}", valueStyle);
                        break;
                    case 3:
                        GUILayout.Label("Custom<long>", titleStyle);
                        GUILayout.Label($"{_customLongValue}", valueStyle);
                        break;
                    case 4:
                        GUILayout.Label("Custom<Vector3>", titleStyle);
                        GUILayout.Label($"({_customVec3Value.x:F4}, {_customVec3Value.y:F4}, {_customVec3Value.z:F4})", valueStyle);
                        break;
                }

                GUILayout.EndArea();
            }
        }

        #endregion
    }
}
