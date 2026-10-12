using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace BossGatedPortals
{
    /// <summary>
    /// TargetPortal's portal map: a click picks the map pin nearest the cursor, and when that is some other pin
    /// (a marker placed on or next to the portal) the click does nothing. Here its portal pins win over other
    /// pins in reach while it looks up the clicked portal (Map.GetClosestPortal, private), and are drawn on top
    /// while its portal map is open. Ordinary map clicks are untouched. Skipped when TargetPortal isn't installed.
    /// </summary>
    internal static class TargetPortalPins
    {
        private static bool resolved;
        internal static MethodBase lookup;
        private static FieldInfo activePins, teleporting;
        internal static bool inLookup;

        // Harmony may call Prepare more than once: look up (and warn) only the first time.
        internal static bool Resolve()
        {
            if (resolved) return lookup != null;
            resolved = true;
            Type map = Tames.PluginType(TargetPortalClickPatch.TargetPortalGUID, "TargetPortal.Map", out bool installed);
            if (map == null) return false;
            activePins = AccessTools.Field(map, "activePins");
            teleporting = AccessTools.Field(map, "Teleporting");
            MethodBase method = AccessTools.Method(map, "GetClosestPortal");
            if (method != null && activePins?.FieldType == typeof(Dictionary<Minimap.PinData, ZDO>)
                && teleporting?.FieldType == typeof(bool))
                lookup = method;
            else if (installed)
                Jotunn.Logger.LogWarning("TargetPortal's portal map changed (TargetPortal update?). Its portal icons " +
                    "are no longer kept clickable under other map pins.");
            return lookup != null;
        }

        private static Dictionary<Minimap.PinData, ZDO> Pins() => (Dictionary<Minimap.PinData, ZDO>)activePins.GetValue(null);

        private static bool Shown(Minimap.PinData pin) => pin.m_uiElement && pin.m_uiElement.gameObject.activeInHierarchy;

        /// <summary>If the pin found isn't a portal, the nearest portal pin in reach replaces it.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void PreferPortal(Vector3 pos, float radius, bool mustBeVisible, ref Minimap.PinData result)
        {
            Dictionary<Minimap.PinData, ZDO> pins = Pins();
            if (pins == null || (result != null && pins.ContainsKey(result))) return;
            Minimap.PinData best = null;
            float bestDistance = radius;
            foreach (Minimap.PinData pin in pins.Keys)
            {
                if (mustBeVisible && !Shown(pin)) continue;
                float distance = Utils.DistanceXZ(pos, pin.m_pos);
                if (distance < bestDistance)
                {
                    best = pin;
                    bestDistance = distance;
                }
            }
            if (best != null) result = best;
        }

        /// <summary>
        /// Moves the portal icons after the other pins (drawn last = on top) while the portal map is open.
        /// Only moves an icon that isn't among the last ones already, so this settles and then does nothing.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void BringToFront()
        {
            if (!(bool)teleporting.GetValue(null)) return;
            Dictionary<Minimap.PinData, ZDO> pins = Pins();
            if (pins == null) return;
            int shown = 0;
            foreach (Minimap.PinData pin in pins.Keys)
                if (Shown(pin)) shown++;
            foreach (Minimap.PinData pin in pins.Keys)
            {
                if (!Shown(pin)) continue;
                RectTransform icon = pin.m_uiElement;
                if (icon.GetSiblingIndex() < icon.parent.childCount - shown) icon.SetAsLastSibling();
            }
        }
    }

    /// <summary>Marks TargetPortal's portal lookup, so the pin search below favours its portal pins.</summary>
    [HarmonyPatch]
    internal static class TargetPortalLookupPatch
    {
        private static bool Prepare() => TargetPortalPins.Resolve();

        private static MethodBase TargetMethod() => TargetPortalPins.lookup;

        private static void Prefix() => TargetPortalPins.inLookup = true;

        private static void Finalizer() => TargetPortalPins.inLookup = false;
    }

    /// <summary>
    /// Runs last: ServerSideMap's postfix on the same method can swap in a shared pin, which a portal pin
    /// should still beat.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), "GetClosestPin", new[] { typeof(Vector3), typeof(float), typeof(bool) })]
    internal static class ClosestPinPatch
    {
        private static bool Prepare() => TargetPortalPins.Resolve();

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Vector3 pos, float radius, bool mustBeVisible, ref Minimap.PinData __result)
        {
            if (!TargetPortalPins.inLookup) return;
            try
            {
                TargetPortalPins.PreferPortal(pos, radius, mustBeVisible, ref __result);
            }
            catch (Exception e)
            {
                Failsafe.Report("TargetPortal portal icon click", e);
            }
        }
    }

    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class PortalIconsOnTopPatch
    {
        private static bool Prepare() => TargetPortalPins.Resolve();

        private static void Postfix()
        {
            try
            {
                TargetPortalPins.BringToFront();
            }
            catch (Exception e)
            {
                Failsafe.Report("TargetPortal portal icons on top", e);
            }
        }
    }
}
