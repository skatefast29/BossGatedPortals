using System;
using System.IO;
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
    // Load after Waypoints when it's installed, so its item check can be found (see Tames.cs).
    [BepInDependency(Tames.WaypointsGUID, BepInDependency.DependencyFlags.SoftDependency)]
    // Every client must have the mod, with the server's major.minor version; patch versions may differ
    // (Jotunn's recommendation). Bump the minor version for anything that changes how client and server
    // must agree (config meaning, synced settings, gate rules); keep bug fixes to patch versions.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal class BossGatedPortals : BaseUnityPlugin
    {
        // Names this mod's config file and is how Jotunn matches versions between server and clients.
        // Changed in 1.0.0 (which every player had to update to anyway); never change it again.
        public const string PluginGUID = "BarryWhite.BossGatedPortals";
        public const string PluginName = "BossGatedPortals Plus";
        // The ID before 1.0.0: its config file is carried over once (see CarryOverOldConfigFile).
        private const string OldGUID = "com.jtmill01.bossgatedportals";
        public const string PluginVersion = "1.0.0";

        public const string XPortalGUID = "yay.spikehimself.xportal";

        // Our own Harmony ID, separate from XPortal's.
        private readonly Harmony harmony = new Harmony(PluginGUID + ".harmony");

        private void Awake()
        {
            CarryOverOldConfigFile();
            Settings.Bind(Config);
            Settings.LogSummary();

            // Keep the parsed tiers current: quietly on every change, with a log after a server
            // sync or after an admin closes the F1 settings window.
            Config.SettingChanged += (_, __) => Settings.Parse();
            SynchronizationManager.OnConfigurationSynchronized += (_, __) => ReportSettings();
            SynchronizationManager.OnConfigurationWindowClosed += ReportSettings;
            // Item IDs can only be checked once the game's item list exists.
            ItemManager.OnItemsRegistered += Settings.Validate;

            PatchEachHook();
            CommandManager.Instance.AddConsoleCommand(new StatusCommand());

            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        // Start runs after every plugin's Awake, so all installed mods are known by now.
        private void Start()
        {
            Compatibility.LogInstalledMods();
            Travel.LogStandDown();
            PortalSpeed.LogStandDown();
        }

        /// <summary>
        /// The config file is named after the plugin ID, which changed in 1.0.0. The first time this version
        /// starts, the old file's settings become the new file's (renamed settings are then carried over by
        /// Settings.Migrate), and the old file is renamed to a backup so it isn't read again.
        /// </summary>
        private void CarryOverOldConfigFile()
        {
            try
            {
                string old = Path.Combine(BepInEx.Paths.ConfigPath, OldGUID + ".cfg");
                if (!File.Exists(old) || File.Exists(Config.ConfigFilePath)) return;
                File.Copy(old, Config.ConfigFilePath);
                Config.Reload();
                File.Move(old, Path.Combine(BepInEx.Paths.ConfigPath, PluginGUID + ".cfg.before-1.0.0"));
                Jotunn.Logger.LogInfo($"Settings carried over from the old config file to {Path.GetFileName(Config.ConfigFilePath)}.");
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"Couldn't carry over the old config file; starting from defaults. {e.Message}");
            }
        }

        /// <summary>
        /// Same as harmony.PatchAll(), one hook at a time. If a game update renames a method we hook,
        /// only that hook is skipped (with an error in the log); PatchAll would stop at the first failure
        /// and leave the rest of the mod half-loaded.
        /// </summary>
        private void PatchEachHook()
        {
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(BossGatedPortals).Assembly))
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Jotunn.Logger.LogError($"Couldn't hook the game for {type.Name} (game update?). " +
                        $"That feature stays vanilla; the rest of the mod still works. {e}");
                }
            }
        }

        private static void ReportSettings()
        {
            Settings.Parse();
            Settings.LogSummary();
            Settings.Validate();
        }

        // No OnDestroy/UnpatchSelf: the Valheim modding wiki advises against unpatching on shutdown.
    }
}
