namespace Moirai.Atropos.Screens
{
    public interface IUIWindowCatalog
    {
        bool TryGet(string windowId, out UIWindowDescriptor descriptor);
    }
}
