using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace polymeric.BetterSignalCompiler;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin {
    internal static Plugin Singleton = null!;

    public ManualLogSource logger;
    public Harmony harmony;

    public Plugin() {
        this.logger = base.Logger;
        this.harmony = new(MyPluginInfo.PLUGIN_GUID);
    }

    private void Awake() {
        Singleton = new();

        Singleton.logger.LogInfo($"Hello from {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION}!");

        HarmonyPatches.PatchInClassShallow(Singleton.harmony, typeof(HarmonyPatches));
    }
}
