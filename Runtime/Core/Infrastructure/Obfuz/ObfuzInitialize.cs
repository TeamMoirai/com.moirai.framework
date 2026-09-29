#if OBFUZ_INSTALLED && ENABLE_OBFUZ
using Obfuz;
using Obfuz.EncryptionVM;
using UnityEngine;

namespace Moirai.Atropos.Obfuz
{
    public class ObfuzInitialize
    {
        /// <summary>
        /// 初始化 <c>EncryptionService</c>，被混淆的代码依赖它才能运行。
        /// </summary>
        /// <remarks>在 <c>AfterAssembliesLoaded</c> 相位尽早执行。</remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void SetUpStaticSecretKey()
        {
            LogUtility.Info("Enable Obfuz");
            LogUtility.Info("SetUpStaticSecret begin");
            EncryptionService<DefaultStaticEncryptionScope>.Encryptor = new GeneratedEncryptionVirtualMachine(Resources.Load<TextAsset>("Obfuz/defaultStaticSecretKey").bytes);
            LogUtility.Info("SetUpStaticSecret end");
        }
    }
}
#endif