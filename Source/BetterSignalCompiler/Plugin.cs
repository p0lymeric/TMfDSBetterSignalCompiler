using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace polymeric.BetterSignalCompiler;

public enum ConfigErrorFormat {
    TraceAndCount,
    TraceAndVerbose,
    Verbose,
}

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin {
    internal static Plugin Singleton = null!;

    public ManualLogSource logger;
    public Harmony harmony;

    public ConfigEntry<bool> configParseBareIds;
    public ConfigEntry<ConfigErrorFormat> configErrorFormat;

    private Plugin() {
        this.logger = base.Logger;
        this.harmony = new(MyPluginInfo.PLUGIN_GUID);

        // TODO investigate live config reloads
        this.configParseBareIds = this.Config.Bind(
            "General",
            "ParseBareIds",
            true,
            "Enables parsing expressions of the form '|<integer>', which specify frequency offsets directly, rather than through dictionary words or digits."
        );
        this.configErrorFormat = this.Config.Bind(
            "General",
            "ConfigErrorFormat",
            ConfigErrorFormat.TraceAndCount,
            "Selects a formatting style for displaying compilation errors.\nTraceAndCount: Shows a compile trace, followed by a count of each error type.\nTraceAndVerbose: Shows a compile trace, followed by every error.\nVerbose: Shows every error, like the base game compiler."
        );
    }

    private void Awake() {
        Singleton = this;

        Singleton.logger.LogInfo($"Hello from {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION}!");

        HarmonyPatches.PatchInClassShallow(Singleton.harmony, typeof(HarmonyPatches));
    }
}
