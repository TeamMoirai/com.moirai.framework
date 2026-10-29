namespace Moirai.Atropos.Diagnostics
{
    public static class DebuggerGate
    {
        public static bool ShouldEnable(bool developmentBuild, bool commandLineRequested,
            bool settingEnabled, bool compositionForce)
        {
            if (!developmentBuild) return false;
            return commandLineRequested || settingEnabled || compositionForce;
        }
    }
}
