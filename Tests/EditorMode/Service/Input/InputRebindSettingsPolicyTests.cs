using Moirai.Atropos.Input;
using NUnit.Framework;

namespace Service.Input
{
    /// <summary>
    /// 重绑定持久化政策的门禁：键名拼装与「路径[|部件名]」编码解码只在这里定义一次。
    /// </summary>
    /// <remarks>
    /// 编解码是纯字符串逻辑，不触碰 <see cref="GameSettings"/> 存储形态；键名经格式项拼装只断形状。
    /// </remarks>
    [TestFixture]
    public sealed class InputRebindSettingsPolicyTests
    {
        [Test]
        public void BindingKey_FormatsActionName()
        {
            Assert.AreEqual("Setting.Input.Rebind.Player/Jump",
                InputRebindSettingsPolicy.BindingKey("Player/Jump"));
            Assert.AreEqual("Setting.Input.Rebind.Submit",
                InputRebindSettingsPolicy.BindingKey("Submit"), "全局动作无分组段");
        }

        [Test]
        public void Encode_SingleBinding_KeepsPathOnly()
        {
            string encoded = InputRebindSettingsPolicy.Encode(new InputBindingData("<Keyboard>/e"));
            Assert.AreEqual("<Keyboard>/e", encoded);
        }

        [Test]
        public void Encode_CompositePart_AppendsPartName()
        {
            string encoded = InputRebindSettingsPolicy.Encode(new InputBindingData("<Keyboard>/w", "up"));
            Assert.AreEqual("<Keyboard>/w|up", encoded);
        }

        [Test]
        public void Encode_EmptyPath_ReturnsNull()
        {
            Assert.IsNull(InputRebindSettingsPolicy.Encode(default), "空快照不落盘");
        }

        [Test]
        public void Encode_PartNameContainsSeparator_ReturnsNull()
        {
            Assert.IsNull(InputRebindSettingsPolicy.Encode(new InputBindingData("<Keyboard>/a", "up|down")),
                "部件名含分隔符会破坏解码，按不支持处理");
        }

        [Test]
        public void TryDecode_SingleBinding_RestoresPath()
        {
            Assert.IsTrue(InputRebindSettingsPolicy.TryDecode("<Keyboard>/e", out var data));
            Assert.AreEqual("<Keyboard>/e", data.Path);
            Assert.IsNull(data.PartName);
        }

        [Test]
        public void TryDecode_CompositePart_RestoresPartName()
        {
            Assert.IsTrue(InputRebindSettingsPolicy.TryDecode("<Keyboard>/w|up", out var data));
            Assert.AreEqual("<Keyboard>/w", data.Path);
            Assert.AreEqual("up", data.PartName);
        }

        [Test]
        public void TryDecode_InvalidValues_ReturnsFalse()
        {
            Assert.IsFalse(InputRebindSettingsPolicy.TryDecode(null, out _), "无键不解码");
            Assert.IsFalse(InputRebindSettingsPolicy.TryDecode("", out _), "空值不解码");
            Assert.IsFalse(InputRebindSettingsPolicy.TryDecode("<Keyboard>/a|up|down", out _), "多段为无效值");
        }

        [Test]
        public void EncodeDecode_RoundTrip_KeepsData()
        {
            var original = new InputBindingData("<Gamepad>/buttonSouth");
            string encoded = InputRebindSettingsPolicy.Encode(in original);
            Assert.IsTrue(InputRebindSettingsPolicy.TryDecode(encoded, out var decoded));
            Assert.AreEqual(original, decoded, "IEquatable 值等价往返");
        }
    }
}
