#if OBFUZ_INSTALLED && ENABLE_OBFUZ
using Obfuz;
using Obfuz.EncryptionVM;
using UnityEngine;

namespace Moirai.Atropos.Obfuz
{
    public class ObfuzInitialize
    {
        /// <summary>
        /// 初始化EncryptionService后被混淆的代码才能正常运行，
        /// 因此尽可能地早地初始化它。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void SetUpStaticSecretKey()
        {
            Debug.Log("Enable Obfuz");
            Debug.Log("SetUpStaticSecret begin");
            EncryptionService<DefaultStaticEncryptionScope>.Encryptor = new GeneratedEncryptionVirtualMachine(Resources.Load<TextAsset>("Obfuz/defaultStaticSecretKey").bytes);
            Debug.Log("SetUpStaticSecret end");
        }
    }
}
#endif