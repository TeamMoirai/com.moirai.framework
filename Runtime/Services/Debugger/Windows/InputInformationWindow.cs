using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 输入信息窗口（Input System 合并视图：设备摘要、触摸、加速度、陀螺仪与磁场传感器）。
    /// <para>项目以 Input System 为唯输入后端（旧 <see cref="UnityEngine.Input"/> API 已禁用）——传感器经设备模型读取，未连接设备显示占位说明。</para>
    /// </summary>
    public sealed class InputInformationWindow : PollingDebuggerWindowBase
    {
        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            BuildDeviceSummarySection(root);
            BuildTouchSection(root);
            BuildSensorSection(root);
        }

        #endregion

        #region 分区 [SECTIONS]

        private static void BuildDeviceSummarySection(VisualElement root)
        {
            VisualElement card = AddSection(root, "Input Summary");
#if ENABLE_INPUT_SYSTEM
            AddRow(card, "Mouse Present", (Mouse.current != null).ToString());
            AddRow(card, "Mouse Position", Mouse.current != null ? Mouse.current.position.ReadValue().ToString() : "No mouse");
            AddRow(card, "Mouse Scroll Delta", Mouse.current != null ? Mouse.current.scroll.ReadValue().ToString() : "No scroll");
            AddRow(card, "Keyboard Present", (Keyboard.current != null).ToString());
            AddRow(card, "Any Key", Keyboard.current != null && Keyboard.current.anyKey.isPressed ? "true" : "false");
            AddRow(card, "Any Key Down", Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame ? "true" : "false");
            AddRow(card, "Touchscreen Present", (Touchscreen.current != null).ToString());
            AddRow(card, "Gamepad Present", (Gamepad.current != null).ToString());
            AddRow(card, "Accelerometer Present", (Accelerometer.current != null).ToString());
            AddRow(card, "Gyroscope Present", (UnityEngine.InputSystem.Gyroscope.current != null).ToString());
            AddRow(card, "Magnetic Field Sensor Present", (MagneticFieldSensor.current != null).ToString());
#else
            AddRow(card, "Input System 未启用", "项目需启用 Input System 包后查看输入信息。");
#endif
        }

        private static void BuildTouchSection(VisualElement root)
        {
            VisualElement card = AddSection(root, "Touch Information");
#if ENABLE_INPUT_SYSTEM
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                AddRow(card, "Touchscreen", "No touchscreen device");
                return;
            }

            var touches = touchscreen.touches;
            int activeCount = 0;
            int totalActiveCount = 0;
            for (int i = 0; i < touches.Count; i++)
            {
                if (touches[i].isInProgress)
                {
                    totalActiveCount++;
                }
            }

            AddRow(card, "Touch Count", totalActiveCount.ToString());
            for (int i = 0; i < touches.Count && activeCount < 8; i++)
            {
                if (!touches[i].isInProgress)
                {
                    continue;
                }

                activeCount++;
                AddRow(card, StringUtility.Format("Touch {0}", i), StringUtility.Format("Pos {0} Phase {1} Pressure {2:F2}",
                    touches[i].position.ReadValue(),
                    touches[i].phase.ReadValue(),
                    touches[i].pressure.ReadValue()));
            }
#else
            AddRow(card, "Input System 未启用", "项目需启用 Input System 包后查看触摸信息。");
#endif
        }

        private static void BuildSensorSection(VisualElement root)
        {
#if ENABLE_INPUT_SYSTEM
            BuildAccelerationSection(root);
            BuildGyroscopeSection(root);
            BuildMagneticFieldSection(root);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static void BuildAccelerationSection(VisualElement root)
        {
            VisualElement card = AddSection(root, "Acceleration Information");
            Accelerometer accelerometer = Accelerometer.current;
            if (accelerometer == null)
            {
                AddRow(card, "Accelerometer", "No accelerometer device");
                return;
            }

            AddRow(card, "Enabled", accelerometer.enabled.ToString());
            AddRow(card, "Acceleration", accelerometer.acceleration.ReadValue().ToString("F4"));

            VisualElement buttonRow = DebuggerUI.CreateToolbarRow();
            buttonRow.Add(DebuggerUI.CreateActionButton("Enable", () => InputSystem.EnableDevice(Accelerometer.current), DebuggerUI.EButtonStyle.Positive));
            buttonRow.Add(DebuggerUI.CreateActionButton("Disable", () => InputSystem.DisableDevice(Accelerometer.current), DebuggerUI.EButtonStyle.Danger));
            card.Add(buttonRow);
        }

        private static void BuildGyroscopeSection(VisualElement root)
        {
            VisualElement card = AddSection(root, "Gyroscope Information");
            UnityEngine.InputSystem.Gyroscope gyroscope = UnityEngine.InputSystem.Gyroscope.current;
            if (gyroscope == null)
            {
                AddRow(card, "Gyroscope", "No gyroscope device");
                return;
            }

            AddRow(card, "Enabled", gyroscope.enabled.ToString());
            AddRow(card, "Angular Velocity", gyroscope.angularVelocity.ReadValue().ToString("F4"));
            AttitudeSensor attitudeSensor = AttitudeSensor.current;
            if (attitudeSensor != null && attitudeSensor.enabled)
            {
                AddRow(card, "Attitude", attitudeSensor.attitude.ReadValue().eulerAngles.ToString("F2"));
            }

            VisualElement buttonRow = DebuggerUI.CreateToolbarRow();
            buttonRow.Add(DebuggerUI.CreateActionButton("Enable", () =>
            {
                InputSystem.EnableDevice(UnityEngine.InputSystem.Gyroscope.current);
                if (AttitudeSensor.current != null)
                {
                    InputSystem.EnableDevice(AttitudeSensor.current);
                }
            }, DebuggerUI.EButtonStyle.Positive));
            buttonRow.Add(DebuggerUI.CreateActionButton("Disable", () =>
            {
                InputSystem.DisableDevice(UnityEngine.InputSystem.Gyroscope.current);
                if (AttitudeSensor.current != null)
                {
                    InputSystem.DisableDevice(AttitudeSensor.current);
                }
            }, DebuggerUI.EButtonStyle.Danger));
            card.Add(buttonRow);
        }

        private static void BuildMagneticFieldSection(VisualElement root)
        {
            VisualElement card = AddSection(root, "Magnetic Field Information");
            MagneticFieldSensor sensor = MagneticFieldSensor.current;
            if (sensor == null)
            {
                AddRow(card, "Magnetic Field Sensor", "No magnetic field sensor device");
                return;
            }

            AddRow(card, "Enabled", sensor.enabled.ToString());
            AddRow(card, "Magnetic Field (µT)", sensor.magneticField.ReadValue().ToString("F4"));
            AddRow(card, "Heading", "Input System 未提供罗盘朝向角（仅原始磁场强度）");

            VisualElement buttonRow = DebuggerUI.CreateToolbarRow();
            buttonRow.Add(DebuggerUI.CreateActionButton("Enable", () => InputSystem.EnableDevice(MagneticFieldSensor.current), DebuggerUI.EButtonStyle.Positive));
            buttonRow.Add(DebuggerUI.CreateActionButton("Disable", () => InputSystem.DisableDevice(MagneticFieldSensor.current), DebuggerUI.EButtonStyle.Danger));
            card.Add(buttonRow);
        }
#endif

        #endregion
    }
}
