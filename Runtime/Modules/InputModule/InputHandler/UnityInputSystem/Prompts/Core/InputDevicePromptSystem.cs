#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Moirai.Atropos.Input.Prompts
{
    /// <summary>
    /// 设备类型的枚举
    /// TODO - 删除，使用更有效的 InputSystem types?
    /// </summary>
    public enum InputDeviceType
    {
        Mouse,
        Keyboard,
        GamePad,
        Touchscreen
    }
    
    /// <summary>
    /// 封装绑定映射项
    /// </summary>
    public class ActionBindingMapEntry
    {
        public string BindingPath;
        public bool IsComposite;
        public bool IsPartOfComposite;
        public string CompositeName;
    }
    
    public static class InputDevicePromptSystem
    {
        
        /// <summary>
        /// 动作路径的映射（例如，“Player/Move”到绑定映射条目，例如“Gamepad/leftStick”）
        /// </summary>
        private static Dictionary<string, List<ActionBindingMapEntry>> s_ActionBindingMap = new Dictionary<string, List<ActionBindingMapEntry>>();
        
        /// <summary>
        /// 设备名称（例如“DualShockGamepadHID”）到设备提示数据（动作绑定和精灵列表）的映射
        /// </summary>
        private static Dictionary<string, GlyphMap> s_DeviceDataBindingMap = new Dictionary<string, GlyphMap>();
        
        /// <summary>
        /// 当前是否已初始化
        /// </summary>
        private static bool s_Initialized = false;
        
        /// <summary>
        /// 设置文件
        /// </summary>
        private static InputSystemDevicePromptSettings s_Settings;
        
        /// <summary>
        /// 当前活动设备
        /// </summary>
        private static InputDevice s_ActiveDevice;
        
        /// <summary>
        /// 当活动设备更改时委派
        /// </summary>
        public static Action<InputDevice> OnActiveDeviceChanged = delegate {  };
        
        /// <summary>
        /// 输入系统上按钮按下的事件侦听器
        /// </summary>
        private static IDisposable s_EventListener;

        private static GlyphMap s_PlatformDeviceOverride;

        public static bool GetPlatformDeviceOverride(out GlyphMap inputDevice)
        {
            if (s_PlatformDeviceOverride != null)
            {
                inputDevice = s_PlatformDeviceOverride;
                return true;
            }

            // 获取当前平台
            var platform = Application.platform;
            // 检查是否有平台覆盖
            foreach (var platformOverride in s_Settings.RuntimePlatformsOverride)
            {
                if (platformOverride.platform == platform)
                { 
                    inputDevice = platformOverride.devicePromptData;
                    return true;
                }
            }

            inputDevice = null;
            return false;
        }
        
        /// <summary>
        /// 初始化数据结构和加载设置，首次使用时调用
        /// </summary>
        private static void Initialize()
        {
            Log.Info("Initialising InputDevicePromptSystem");
            s_Settings = InputSystemDevicePromptSettings.Instance;
            
            if (s_Settings == null)
            {
                Log.Warning("InputSystemDevicePromptSettings missing");
                return;
            }
            
            if (s_Settings.GlyphCollection == null)
            {
                Log.Warning("Glyph Collection missing");
                return;
            }

            if (!s_Settings.PromptSpriteFormatter.Contains(InputSystemDevicePromptSettings.PROMPT_SPRITE_FORMATTER_SPRITE_PLACEHOLDER))
            {
                Log.Error($"{nameof(InputSystemDevicePromptSettings.PromptSpriteFormatter)} must include {InputSystemDevicePromptSettings.PROMPT_SPRITE_FORMATTER_SPRITE_PLACEHOLDER} or no sprites will be shown.");
            }
            
            // 监听任意设备上按下的按钮，以便动态切换设备提示（来自 InputSystem.cs 中的描述）
            s_EventListener = InputSystem.onAnyButtonPress.Call(OnButtonPressed);
            
            // 监听设备更改。如果活动设备已断开连接，则切换到默认设备
            InputSystem.onDeviceChange += OnDeviceChange;
            
            BuildBindingMaps();
            FindDefaultDevice();

            GetPlatformDeviceOverride(out s_PlatformDeviceOverride);

            s_Initialized = true;
        }

        /// <summary>
        /// 在设备更改时调用
        /// </summary>
        /// <param name="device"></param>
        /// <param name="change"></param>
        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            // 如果活动设备已断开连接，则恢复为默认设备
            if (device != s_ActiveDevice) return;
            
            if ((change == InputDeviceChange.Disconnected) || (change == InputDeviceChange.Removed))
            {
                FindDefaultDevice();
                // 通知更改
                OnActiveDeviceChanged.Invoke(s_ActiveDevice);
            }
        }
        
        // 预编译正则表达式
        private static readonly Regex s_ActionTagRegex = new Regex(
            $@"\{InputSystemDevicePromptSettings.OPEN_TAG}(.*?)\{InputSystemDevicePromptSettings.CLOSE_TAG}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
        /// <summary>
        /// 将给定字符串中的标记替换为 TMPPro 字符串以插入设备提示 sprite
        /// </summary>
        /// <param name="inputText"></param>
        /// <param name="isComposite">如果按键动作为复合，是否尝试获取合成后的图标</param>
        /// <returns></returns>
        public static string InsertPromptSprites(string inputText, bool isComposite)
        {
            if (!s_Initialized) Initialize();
            if (!s_Initialized) return "InputSystemDevicePrompt Settings missing - please create using menu item 'Window/Input System Device Prompts/Create Settings'";

            var replacedText = inputText;
            
            // 使用正则表达式匹配 {action:...}
            var matches = s_ActionTagRegex.Matches(inputText);
            foreach (Match match in matches)
            {
                string actionName = match.Groups[1].Value.Trim();
                
                var replacementTagText = GetActionPathBindingTextSpriteTags(actionName, isComposite);
                
                // 如果 PromptSpriteFormatter 由于某种原因为空，则返回文本，就像 formatter 是 {SPRITE}（默认）
                var promptSpriteFormatter = s_Settings.PromptSpriteFormatter == "" ? InputSystemDevicePromptSettings.PROMPT_SPRITE_FORMATTER_SPRITE_PLACEHOLDER : s_Settings.PromptSpriteFormatter;
                // 设置中的 PromptSpriteFormatter 使用 {SPRITE} 作为 sprite 的占位符，将其转换为 {0} 作为 string.Format
                promptSpriteFormatter = promptSpriteFormatter.Replace(InputSystemDevicePromptSettings.PROMPT_SPRITE_FORMATTER_SPRITE_PLACEHOLDER, "{0}");
                replacementTagText = string.Format(promptSpriteFormatter, replacementTagText);

                replacedText = replacedText.Replace($"{InputSystemDevicePromptSettings.OPEN_TAG}{actionName}{InputSystemDevicePromptSettings.CLOSE_TAG}", replacementTagText);
            }

            return replacedText;
        }
        
        /// <summary>
        /// 获取给定输入标签（例如 “Player/Jump”）的第一个匹配精灵（例如 DualShock Cross Button Sprite）
        /// </summary>
        /// <param name="inputTag"></param>
        /// <param name="isComposite">如果按键动作为复合，是否尝试获取合成后的图标</param>
        /// <returns></returns>
        /// <remarks>不支持复合标签。例如 WASD，如果 <see cref="isComposite"/> = <c>false</c>，会只返回第一个 W，建议将 <see cref="isComposite"/> 设为 <c>true</c></remarks>
        public static Sprite GetActionPathBindingSprite(string inputTag, bool isComposite)
        {
            if (!s_Initialized) Initialize();

            if (s_PlatformDeviceOverride == null) // 非平台覆盖
            {
                if (s_ActiveDevice == null) return null; // NO_ACTIVE_DEVICE
                var activeDeviceName = s_ActiveDevice.name;
                
                // 当未连接指定设备时显示的图标
                if (!s_DeviceDataBindingMap.ContainsKey(activeDeviceName))
                {
                    var prompt = s_Settings.GlyphCollection?.DisconnectGlyph;
                    return prompt?.Icon == null ? null : prompt.Icon;
                }
            }
            
            string modifier = GetModifier(ref inputTag);
            // Log.Info($"inputTag:{inputTag} modifier:{modifier}");

            var lowerCaseTag = inputTag.ToLower();

            // 当 InputSystem 不存在指定 action 时的图标
            if (!s_ActionBindingMap.ContainsKey(lowerCaseTag))
            {
                var prompt = s_Settings.GlyphCollection?.NullGlyph;
                return prompt?.Icon == null ? null : prompt.Icon;
            }

            var (validDevice, matchingPrompt) = GetActionPathBindingPromptEntries(inputTag, modifier, isComposite);

            // 当 action 有效，但该 action 的没有输入提示时
            if (matchingPrompt == null || matchingPrompt.Count == 0)
            {
                var prompt = s_Settings.GlyphCollection?.UnboundGlyph;
                return prompt?.Icon == null ? null : prompt.Icon;
            }
            
            // 默认返回第一个
            return matchingPrompt[0].Icon;
        }

        /// <summary>
        /// 获取给定精灵名称的 DeviceSpriteEntries 列表中的当前活动设备匹配 sprite
        /// </summary>
        /// <param name="spriteName"></param>
        /// <returns></returns>
        public static Sprite GetDeviceSprite(string spriteName)
        {
            if (!s_Initialized) Initialize();

            GlyphMap validDevice;

            if (s_PlatformDeviceOverride != null)
            {
                validDevice = s_PlatformDeviceOverride;
            }
            else
            {
                if (s_ActiveDevice == null) return null;

                var activeDeviceName = s_ActiveDevice.name;

                if (!s_DeviceDataBindingMap.ContainsKey(activeDeviceName))
                {
                    Log.Error($"MISSING_DEVICE_ENTRIES '{activeDeviceName}'");
                    return null;
                }

                // 在字典 s_DeviceDataBindingMap 中搜索以 activeDeviceName 开头的 key
                // var matchingDevice = s_DeviceDataBindingMap.FirstOrDefault(x => x.Key.StartsWith(activeDeviceName)).Value;

                validDevice = s_DeviceDataBindingMap[activeDeviceName];
            }
            

            var matchingSprite = validDevice.DeviceGlyphs.FirstOrDefault((sprite) =>
                           String.Equals(sprite.DeviceName, spriteName, StringComparison.CurrentCultureIgnoreCase));

            if (matchingSprite != null)
            {
                return matchingSprite.DeviceSprite;
            }

            return null;
        }

        /// <summary>
        /// 为给定标记的所有匹配 sprite 创建 TextMeshPro 格式字符串
        /// </summary>
        /// <param name="inputTag"></param>
        /// <param name="isComposite">如果按键动作为复合，是否尝试获取合成后的图标</param>
        /// <returns></returns>
        /// <remarks>支持复合标签。如果 <see cref="isComposite"/> = <c>false</c>。则返回活动设备的所有匹配项（按顺序）。例如 WASD，则会返回 4 个 TextSprite</remarks>
        private static string GetActionPathBindingTextSpriteTags(string inputTag, bool isComposite = false)
        {
            if (s_PlatformDeviceOverride == null) // 非平台覆盖
            {
                if (s_ActiveDevice == null) return "NO_ACTIVE_DEVICE";
                var activeDeviceName = s_ActiveDevice.name;

                // 当未连接指定设备时显示的图标
                if (!s_DeviceDataBindingMap.ContainsKey(activeDeviceName))
                {
                    var prompt = s_Settings.GlyphCollection?.DisconnectGlyph;
                    if (!string.IsNullOrEmpty(prompt?.TextMeshSpriteSheetName))
                    {
                        return $"<sprite=\"{prompt.TextMeshSpriteSheetName}\" name=\"{prompt.Icon.name}\" {s_Settings.RichTextTags}>";
                    }
                    
                    return $"MISSING_DEVICE_ENTRIES '{activeDeviceName}'";
                }
            }
            
            string modifier = GetModifier(ref inputTag);
            // Log.Info($"inputTag:{inputTag} modifier:{modifier}");
            
            var lowerCaseTag = inputTag.ToLower();
            
            // 当 InputSystem 不存在指定 action 时的图标
            if (!s_ActionBindingMap.ContainsKey(lowerCaseTag))
            {
                var prompt = s_Settings.GlyphCollection?.NullGlyph;
                if (!string.IsNullOrEmpty(prompt?.TextMeshSpriteSheetName))
                {
                    return $"<sprite=\"{prompt.TextMeshSpriteSheetName}\" name=\"{prompt.Icon.name}\" {s_Settings.RichTextTags}>";
                }
                
                return $"MISSING_ACTION {lowerCaseTag}";
            }

            var (validDevice, matchingPrompt) = GetActionPathBindingPromptEntries(inputTag, modifier, isComposite);
           
            // 当 action 有效，但该 action 的没有输入提示时
            if (matchingPrompt == null || matchingPrompt.Count == 0)
            {
                var prompt = s_Settings.GlyphCollection?.UnboundGlyph;
                if (!string.IsNullOrEmpty(prompt?.TextMeshSpriteSheetName))
                {
                    return $"<sprite=\"{prompt.TextMeshSpriteSheetName}\" name=\"{prompt.Icon.name}\" {s_Settings.RichTextTags}>";
                }
                
                return $"MISSING_PROMPT '{inputTag}'";
            }
            
            // 返回每个
            var outputText = string.Empty;
            foreach (var prompt in matchingPrompt)
            {
                PromptGlyph targetPrompt = prompt;
                if (targetPrompt.Icon == null) targetPrompt = s_Settings.GlyphCollection?.NullGlyph;
                if (targetPrompt?.Icon == null) continue;

                outputText += $"<sprite=\"{targetPrompt.TextMeshSpriteSheetName}\" name=\"{targetPrompt.Icon.name}\" {s_Settings.RichTextTags}>";
            }
            return outputText;
        }

        /// <summary>
        /// 获取给定标签的所有匹配提示条目（例如 “Player/Jump”）
        /// </summary>
        /// <param name="inputTag"></param>
        /// <param name="isComposite">如果按键动作为复合，是否尝试获取合成后的图标</param>
        /// <returns></returns>
        private static (GlyphMap validDevice, List<ActionGlyph> validEntries) GetActionPathBindingPromptEntries(string inputTag, string modifier, bool isComposite)
        {
            GlyphMap validDevice;
            
            var lowerCaseTag = inputTag.ToLower();
            if (!s_ActionBindingMap.ContainsKey(lowerCaseTag)) return (null, null);

            if (s_PlatformDeviceOverride != null)
            {
                validDevice = s_PlatformDeviceOverride;
            }
            else
            {
                if (s_ActiveDevice == null) return (null, null);
                if (!s_DeviceDataBindingMap.ContainsKey(s_ActiveDevice.name)) return (null, null);

                validDevice = s_DeviceDataBindingMap[s_ActiveDevice.name];
            }
            
            var validEntries = new List<ActionGlyph>();
            foreach (var actionBinding in s_ActionBindingMap[lowerCaseTag])
            {
                // Log.Info($"Checking binding '{actionBinding.BindingPath}' on device {validDevice.name}");
                ActionGlyph matchingPrompt = null;
                
                bool findComposite = false;
                if (actionBinding.IsComposite)
                {
                    if (isComposite)
                    {
                        matchingPrompt = validDevice.ActionGlyphs.FirstOrDefault((prompt) =>
                            String.Equals(prompt.ActionBindingPath, actionBinding.CompositeName,
                                StringComparison.CurrentCultureIgnoreCase));
                    
                        findComposite = matchingPrompt != null;
                        if (findComposite)
                        {
                            validEntries.Clear();
                        }
                    }
                    
                    // 解析组合按键的指定标签
                    if (matchingPrompt == null && !string.IsNullOrEmpty(modifier))
                    {
                        string bindingPath = actionBinding.CompositeName;
                        GetModifiedActionName(ref bindingPath, modifier);
                        
                        matchingPrompt = validDevice.ActionGlyphs.FirstOrDefault((prompt) =>
                            String.Equals(prompt.ActionBindingPath, bindingPath,
                                StringComparison.CurrentCultureIgnoreCase));
                        
                        if (matchingPrompt != null)
                        {
                            validEntries.Clear();
                        }
                    }
                }
                
                var usage = GetUsageFromBindingPath(actionBinding.BindingPath);
                if (matchingPrompt == null && string.IsNullOrEmpty(usage))
                {
                     matchingPrompt = validDevice.ActionGlyphs.FirstOrDefault((prompt) =>
                        String.Equals(prompt.ActionBindingPath, actionBinding.BindingPath,
                            StringComparison.CurrentCultureIgnoreCase));
                }
                else if (matchingPrompt == null)
                {
                    // 在某些控制方案（例如鼠标键盘）中，活动设备可能没有给定的用法（例如 Submit），因此需要找到替代方案
                    // 例如 “Submit” 或 “Cancel”，格式为 “*/{Submit}” “*/{Cancel}”
                    var matchingUsageFound = false;
                    var deviceList = new List<InputDevice>(InputSystem.devices);
                    // 将活动设备移动到队列前面
                    deviceList.Remove(s_ActiveDevice);
                    deviceList.Insert(0, s_ActiveDevice);

                    for (var i = 0; i < deviceList.Count && !matchingUsageFound; i++)
                    {
                        var testDevice = deviceList[i];
                        foreach (var control in testDevice.allControls)
                        {
                            foreach (var controlUsage in control.usages)
                            {
                                if (controlUsage.ToLower() == usage.ToLower())
                                {
                                    // 匹配！搜索有相同扩展名的提示词条（忽略设备前缀部分，例如 “Gamepad”）
                                    matchingPrompt = validDevice.ActionGlyphs.FirstOrDefault((prompt) =>
                                        String.Equals(prompt.ActionBindingPath.Split('/').Last(), control.name,
                                            StringComparison.CurrentCultureIgnoreCase));
                                    
                                    if (matchingPrompt != null)
                                    {
                                        matchingUsageFound = true;
                                        break;
                                    }
                                }
                            }
                            
                            if (matchingPrompt != null) break;
                        }
                    }
                }
                
                if (matchingPrompt != null)
                {
                    // Log.Info($"Found matching prompt <b>{matchingPrompt.ActionBindingPath}</b> for <b>{inputTag}</b>");
                    validEntries.Add(matchingPrompt);
                    
                    if (findComposite) break;
                }
            }
            
            return (validDevice, validEntries);
        }
        
        /// <summary>
        /// 从绑定路径中提取用法，例如 “*/{Submit}” 返回 “Submit”
        /// </summary>
        /// <param name="actionBinding"></param>
        /// <returns></returns>
        private static string GetUsageFromBindingPath(string actionBinding)
        {
            return actionBinding.Contains("*/{") ? actionBinding.Substring(3, actionBinding.Length - 4) : String.Empty;
        }
        
        private static string GetModifier(ref string actionName)
        {
            int i = actionName.IndexOf(":", StringComparison.CurrentCultureIgnoreCase);
            if (i <= 0) return string.Empty;

            string result = actionName.Substring(i + 1);
            actionName = actionName.Substring(0, i);

            return result.Trim().ToLower();
        }

        /// <summary>
        /// 根据修饰符，获取指定的动作名称。
        /// </summary>
        /// <param name="actionName"></param>
        /// <param name="modifier"></param>
        /// <remarks>必须按照正反、上下左右这样的顺序排列组合按键，确保可以正确解析。</remarks>
        private static void GetModifiedActionName(ref string actionName, string modifier)
        {
            // Log.Info($"composite action name: {actionName}");
            var child = actionName.Split('+');
            switch (modifier)
            {
                case "neg":
                case "up":
                    actionName = child[0];
                    break;
                case "pos":
                case "down":
                    actionName = child[1];
                    break;
                case "left":
                    actionName = child[2];
                    break;
                case "right":
                    actionName = child[3];
                    break;
            }
        }
        
        /// <summary>
        /// 根据当前设置优先级查找默认设备
        /// </summary>
        private static void FindDefaultDevice()
        {
            // 当启动时，没有按下按钮，默认选择与设置文件中优先级匹配的第一个设备
            foreach (var deviceType in s_Settings.DefaultDevicePriority)
            {
                foreach (var device in InputSystem.devices.Where(device => DeviceMatchesType(device, deviceType)))
                {
                    s_ActiveDevice = device;
                    return;
                }
            }
        }

        private static bool DeviceMatchesType(InputDevice device, InputDeviceType type)
        {
            return type switch
            {
                InputDeviceType.Mouse => device is Mouse,
                InputDeviceType.Keyboard => device is Keyboard,
                InputDeviceType.GamePad => device is Gamepad,
                InputDeviceType.Touchscreen => device is Touchscreen,
                _ => false
            };
        }
        
        /// <summary>
        /// 构建所有动作的内部映射（例如，将“Player/Jump”映射到可用的绑定路径，例如“Gamepad/ButtonSouth”）
        /// </summary>
        private static void BuildBindingMaps()
        {
            s_ActionBindingMap = new Dictionary<string, List<ActionBindingMapEntry>>();
            
            // 构建所有控件和关联绑定的映射
            foreach (var inputActionAsset in s_Settings.InputActionAssets)
            {
                var allActionMaps = inputActionAsset.actionMaps;
                string bindingPathLower = string.Empty;
                foreach (var actionMap in allActionMaps)
                {
                    // 单独按键
                    foreach (var binding in actionMap.bindings)
                    {
                        if (binding.isComposite) continue;
                        
                        var bindingPath = $"{actionMap.name}/{binding.action}";
                        bindingPathLower = bindingPath.ToLower();
                        
                        // Log.Info($"Binding <b>{bindingPathLower}</b> to path <b>{binding.path}</b>");
                        var entry = new ActionBindingMapEntry
                        {
                            BindingPath = binding.effectivePath,
                            IsComposite = binding.isComposite,
                            IsPartOfComposite = binding.isPartOfComposite,
                            CompositeName = binding.name,
                        };

                        if (s_ActionBindingMap.TryGetValue(bindingPathLower, out var value))
                        {
                            value.Add(entry);
                        }
                        else
                        {
                            s_ActionBindingMap.Add(bindingPathLower, new List<ActionBindingMapEntry> { entry });
                        }
                    }
                    
                    // 组合按键
                    string compositeName = string.Empty;
                    bool inComposite = false;
                    string compositeBindingPath = string.Empty;
                    string compositePath = string.Empty;
                    foreach (var binding in actionMap.bindings)
                    {
                        bool isAdd = false;
                        if (binding.isPartOfComposite)
                        {
                            if (!inComposite)
                            {
                                inComposite = true;
                                compositeName = binding.path;
                            }
                            else
                            {
                                compositeName += "+" + binding.path;
                            }

                            if (actionMap.bindings.Last() == binding)
                            {
                                isAdd = true;
                            }
                            
                            var bindingPath = $"{actionMap.name}/{binding.action}";
                            compositePath = bindingPath.ToLower();
                        }
                        else
                        {
                            if (binding.isComposite)
                            {
                                var bindingPath = $"{actionMap.name}/{binding.action}";
                                bindingPathLower = string.IsNullOrEmpty(compositePath) ? bindingPath.ToLower() : compositePath;
                                
                                compositeBindingPath = binding.effectivePath;
                            }
                            
                            isAdd = true;
                            inComposite = false;
                        }
                        
                        if (!isAdd || string.IsNullOrEmpty(compositeName)) continue;
                        
                        // Log.Info($"Binding <b>{bindingPathLower}</b> to composite <b>{compositeName}</b>");
                        var entry = new ActionBindingMapEntry
                        {
                            BindingPath = compositeBindingPath,
                            IsComposite = true,
                            IsPartOfComposite = false,
                            CompositeName = compositeName,
                        };
                        
                        if (s_ActionBindingMap.TryGetValue(bindingPathLower, out var value))
                        {
                            value.Add(entry);
                        }
                        else
                        {
                            s_ActionBindingMap.Add(bindingPathLower, new List<ActionBindingMapEntry> { entry });
                        }
                        
                        compositeName = string.Empty;
                        compositePath = string.Empty;
                    }
                }
            }

            // 构建设备名称到设备数据的映射
            foreach (var devicePromptData in s_Settings.GlyphCollection.PromptMaps)
            {
                foreach (var deviceName in devicePromptData.DeviceNames)
                {
                    if (s_DeviceDataBindingMap.ContainsKey(deviceName))
                    {
                        Log.Warning($"Duplicate device name found in InputSystemDevicePromptSettings: {deviceName}. Check your config");
                    }
                    else
                    {
                        s_DeviceDataBindingMap.Add(deviceName, devicePromptData);
                    }
                }
            }
        }
        
        /// <summary>
        /// 在任意设备上按下按钮时调用
        /// </summary>
        /// <param name="button"></param>
        private static void OnButtonPressed(InputControl button)
        {
            if (s_ActiveDevice==button.device) return;
            s_ActiveDevice = button.device;
            OnActiveDeviceChanged.Invoke(s_ActiveDevice);
        }
        
    }
}
#endif