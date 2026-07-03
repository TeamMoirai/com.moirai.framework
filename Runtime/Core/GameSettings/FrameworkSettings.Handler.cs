#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos
{
    public partial class FrameworkSettings
    {
        protected TSub AddSubAsset<TSub>(string subAssetName) where TSub : ScriptableObject
        {
            TSub sub = CreateInstance<TSub>();
            sub.name = subAssetName;

            string mainAssetPath = AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrEmpty(mainAssetPath))
            {
                Debug.LogError("只能对已保存为资产的 Settings 调用 AddSubAsset。");
                DestroyImmediate(sub);
                return null;
            }

            AssetDatabase.AddObjectToAsset(sub, mainAssetPath);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            return sub;
        }

        protected void RemoveSubAsset(ScriptableObject subAsset)
        {
            if (subAsset == null) return;
            if (AssetDatabase.IsSubAsset(subAsset))
                AssetDatabase.RemoveObjectFromAsset(subAsset);

            DestroyImmediate(subAsset, true);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif