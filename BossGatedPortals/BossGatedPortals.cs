using BepInEx;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;

namespace BossGatedPortals
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Load after XPortal when it's installed, but work without it.
    [BepInDependency(XPortalGUID, BepInDependency.DependencyFlags.SoftDependency)]
    // Every client must run exactly the server's version of this mod.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    internal class BossGatedPortals : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jtmill01.bossgatedportals";
        public const string PluginName = "BossGatedPortals";
        public const string PluginVersion = "0.1.1";

        public const string XPortalGUID = "yay.spikehimself.xportal";

        // Our own Harmony ID, separate from XPortal's.
        private readonly Harmony harmony = new Harmony(PluginGUID + ".harmony");

        private void Awake()
        {
            Settings.Bind(Config);
            Settings.LogSummary();

            // Keep the parsed tiers current: quietly on every change, with a log after a server
            // sync or after an admin closes the F1 settings window.
            Config.SettingChanged += (_, __) => Settings.Parse();
            SynchronizationManager.OnConfigurationSynchronized += (_, __) => ReportSettings();
            SynchronizationManager.OnConfigurationWindowClosed += ReportSettings;
            // Item IDs can only be checked once the game's item list exists.
            ItemManager.OnItemsRegistered += Settings.Validate;
            ItemManager.OnItemsRegistered += Hints.LogUnmappedItems;

            harmony.PatchAll();
            CommandManager.Instance.AddConsoleCommand(new StatusCommand());

            bool xportal = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(XPortalGUID);
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} loaded. XPortal detected: {xportal}");
        }

        // Start runs after every plugin's Awake, so all installed mods are known by now.
        private void Start()
        {
            Compatibility.LogInstalledMods();
        }

        private static void ReportSettings()
        {
            Settings.Parse();
            Settings.LogSummary();
            Settings.Validate();
            Hints.LogUnmappedItems();
        }

        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }
    }
}
