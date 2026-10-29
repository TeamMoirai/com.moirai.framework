namespace Moirai.Atropos.Scenes
{
    public interface ISceneLoadOperation
    {
        float Progress { get; }
        void AllowActivation();
        Cysharp.Threading.Tasks.UniTask<ISceneLease> Completion { get; }
    }

    public interface ISceneBackend
    {
        ISceneLoadOperation BeginLoad(Moirai.Atropos.Assets.ResourceKey key,
            UnityEngine.SceneManagement.LoadSceneParameters parameters, bool suspendActivation);
    }
}
