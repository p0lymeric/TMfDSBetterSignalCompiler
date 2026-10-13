using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace polymeric.BetterSignalCompiler;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin {
    internal static Plugin Singleton = null!;

    public ManualLogSource logger;
    public Harmony harmony;

    public ConfigEntry<bool> configParseBareIds;

    private Plugin() {
        this.logger = base.Logger;
        this.harmony = new(MyPluginInfo.PLUGIN_GUID);
        this.configParseBareIds = this.Config.Bind("General", "ParseBareIds", true, "Enables parsing expressions of the form '|<integer>', which specify frequency offsets directly, rather than through dictionary words or digits.");
    }

    private void Awake() {
        Singleton = this;

        Singleton.logger.LogInfo($"Hello from {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION}!");

        HarmonyPatches.PatchInClassShallow(Singleton.harmony, typeof(HarmonyPatches));
    }
}
