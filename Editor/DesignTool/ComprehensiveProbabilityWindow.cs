using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
// ReSharper disable InconsistentNaming

namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 依次递增的概率如何计算期望
    /// </summary>
    /// <remarks>中间是固定次数个p1（初始有固定次数个初始概率），如果固定次数为1 则类似于上面链接的描述</remarks>
    /// <example>
    /// 比如一把武器，攻击造成伤害为1，暴击则翻倍，变成2，现在这把武器初始暴击率10%；
    /// 如果下一次攻击没有暴击，则暴击率增加10%，达到20%；
    /// 如果下一次攻击还没暴击，则变成30%；如果还没暴击，则变成40%；
    /// 以此类推，如果连续9次没有暴击，则暴击率变为100，即必定暴击。
    /// <para>暴击一次后，暴击率回到10%</para>
    /// <para>那么这把武器的伤害期望是多？</para>
    /// </example>
    /// <!--
    /// https://www.zhihu.com/question/452918374
    /// 参考 Timosky 的回答
    /// P0=q
    /// P1=(1-base)*P1
    /// Pn=(1-addValue*n)*Pn-1
    /// p0+p1+p1....+p2+p3...+pn = 1
    /// fixed_count 个 p1
    /// offset_count 个 p2,pn
    /// 算上 p0,总次数为 1+fixed_count+offset_count
    /// -->
    public class ComprehensiveProbabilityWindow: OdinEditorWindow
    {
        [MenuItem("Window/DesignTool/递增概率期望计算")]
        private static void OpenWindow()
        {
            _myWindow = GetWindow<ComprehensiveProbabilityWindow>();
            _myWindow.Show();
        }

        private static ComprehensiveProbabilityWindow _myWindow;

        [InfoBox("算上p0,总次数为(1+固定次数+偏移次数)")]
        [LabelText("输入基础值")]
        public float baseValue = 0.1f;
        [LabelText("输入偏移值")]
        public float addValue = 0.1f;

        #region 求期望

        private const string EXPECTATION_GROUP = "求期望";

        [TabGroup(EXPECTATION_GROUP), LabelText("输入固定次数")]
        public int e_fixedCount = 1;
        [TabGroup(EXPECTATION_GROUP), LabelText("输入偏移次数")]
        public int e_offsetCount = 8;
        [TabGroup(EXPECTATION_GROUP), Button("获取期望值")]
        private void GetResult()
        {
            float factorP0 = 1;
            float factorP1 = 1 - baseValue;
            float factorResult = factorP0;

            for (int i = 0; i < e_fixedCount; i++)
            {
                factorResult = factorResult + factorP1;
            }

            float factor_px = factorP1;
            for (int i = 2; i < e_offsetCount + 2; i++)
            {
                factor_px = (1 - addValue * i) * factor_px;
                factorResult = factorResult + factor_px;
            }
            e_expectedValue = 1.0f / factorResult;
        }

        // Result
        [TabGroup(EXPECTATION_GROUP), LabelText("期望"), ReadOnly, PropertyOrder(100)]
        public float e_expectedValue = 0.1f;

        #endregion

        #region 求次数

        private const string COUNT_GROUP = "求次数";

        [TabGroup(COUNT_GROUP), LabelText("输入期望值")]
        public float c_expectedValue = 0.273208f;
        [TabGroup(COUNT_GROUP), LabelText("输入固定值")]
        public float c_fixedCount = 1;
        [TabGroup(COUNT_GROUP), LabelText("输入偏移次数上限,至少为1"), InfoBox("次数至少为1", InfoMessageType.Error, "IsMaxCountOk")]
        public int c_maxCount = 10000;
        [TabGroup(COUNT_GROUP), LabelText("输入精度偏移")]
        public float c_offsetValue = 0.01f;
        [TabGroup(COUNT_GROUP), Button("获取偏移次数值")]
        private void GetCountResult()
        {
            float p1 = (1 - baseValue) * c_expectedValue;
            float result = c_expectedValue;
            float Px = p1;
            bool isFind = false;

            for (int i = 0; i < c_fixedCount; i++)
            {
                result = result + p1;
            }

            for (int i = 2; i < c_maxCount; i++)
            {
                Px = (1 - addValue * i) * Px;
                result = result + Px;
                if (result >= (1.0f - c_offsetValue))
                {
                    c_offsetCount = i;
                    isFind = true;
                    break;
                }
            }

            if (isFind)
            {
                c_result = "成功，偏移次数值:" + c_offsetCount + "总次数" + (c_offsetCount + 2) + " 趋向于1的结果:" + result;
            }
            else
            {
                c_result = "最大次数下，偏移次数值:" + c_maxCount + "总次数" + (c_offsetCount + 2) + " 趋向于1的结果:" + result;
            }
        }

        public bool IsMaxCountOk()
        {
            return c_maxCount < 1;
        }

        // Result
        [TabGroup(COUNT_GROUP), LabelText("偏移次数"), ReadOnly, PropertyOrder(100)]
        public int c_offsetCount = 0;
        [TabGroup(COUNT_GROUP), LabelText("结果信息"), ReadOnly, PropertyOrder(101)]
        public string c_result = "";

        #endregion
    }
}