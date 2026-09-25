using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace BossGatedPortals
{
    /// <summary>
    /// Makes the item tooltip's "can't be teleported" line and the crossed-out portal icon on
    /// inventory slots agree with the portal gate. Vanilla shows both when the item's m_teleportable
    /// flag is off (and the "teleport all" world modifier isn't set), which would lie once a tier
    /// unlocks. With AccurateTooltips on, they show exactly when Gate.CanTeleport says no for the
    /// local player.
    /// </summary>
    internal static class Tooltips
    {
        /// <summary>Should the tooltip line and slot icon mark this item as not teleportable?</summary>
        public static bool ShowNoTeleport(ItemDrop.ItemData item)
        {
            // No game names in here (see Failsafe): if anything fails, show what vanilla would.
            try
            {
                return Decide(item);
            }
            catch (Exception e)
            {
                Failsafe.Report("Teleport tooltip/icon", e);
                try
                {
                    return Vanilla(item);
                }
                catch
                {
                    return false; // even vanilla's check failed: don't mark the item
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Decide(ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            if (!Settings.Enabled.Value || !Settings.AccurateTooltips.Value || player == null)
                return Vanilla(item);

            return !Gate.CanTeleport(player, item);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Vanilla(ItemDrop.ItemData item) =>
            !item.m_shared.m_teleportable && !Failsafe.TeleportAllSet();

        /// <summary>
        /// In ItemData.GetTooltip, swap vanilla's condition
        ///   if (!item.m_shared.m_teleportable &amp;&amp; !ZoneSystem.instance.GetGlobalKey(GlobalKeys.TeleportAll))
        /// for
        ///   if (Tooltips.ShowNoTeleport(item))
        /// Everything else in the tooltip is untouched. If a game update changes the code, the patch
        /// logs a warning and leaves vanilla as it is.
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        private static class GetTooltipPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                FieldInfo shared = AccessTools.Field(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.m_shared));
                FieldInfo teleportable = AccessTools.Field(typeof(ItemDrop.ItemData.SharedData),
                    nameof(ItemDrop.ItemData.SharedData.m_teleportable));
                MethodInfo getGlobalKey = AccessTools.Method(typeof(ZoneSystem), nameof(ZoneSystem.GetGlobalKey),
                    new[] { typeof(GlobalKeys) });

                var matcher = new CodeMatcher(instructions).MatchStartForward(
                    new CodeMatch(OpCodes.Ldarg_0),
                    new CodeMatch(OpCodes.Ldfld, shared),
                    new CodeMatch(OpCodes.Ldfld, teleportable),
                    new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S),
                    new CodeMatch(OpCodes.Call),        // ZoneSystem.instance
                    new CodeMatch(i => i.opcode == OpCodes.Ldc_I4 || i.opcode == OpCodes.Ldc_I4_S),
                    new CodeMatch(OpCodes.Callvirt, getGlobalKey),
                    new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S),
                    new CodeMatch(OpCodes.Ldsfld),      // m_stringBuilder
                    new CodeMatch(OpCodes.Ldstr, "\n<color=orange>$item_noteleport</color>"));

                if (matcher.IsInvalid)
                {
                    Jotunn.Logger.LogWarning("AccurateTooltips: couldn't find the teleport line in the item " +
                        "tooltip (game update?). Tooltips will show vanilla text.");
                    return instructions;
                }

                // Keep the skip label (the target of vanilla's branches) and the first instruction's labels.
                object skipLabel = matcher.InstructionAt(3).operand;
                matcher
                    .Advance(1)
                    .RemoveInstructions(7)
                    .Insert(
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Tooltips), nameof(ShowNoTeleport))),
                        new CodeInstruction(OpCodes.Brfalse, skipLabel));
                return matcher.InstructionEnumeration();
            }
        }

        /// <summary>
        /// In InventoryGrid.UpdateGui (player inventory and chests), swap vanilla's
        ///   element.m_noteleport.enabled = !item.m_shared.m_teleportable &amp;&amp; !ZoneSystem.instance.GetGlobalKey(GlobalKeys.TeleportAll);
        /// for
        ///   element.m_noteleport.enabled = Tooltips.ShowNoTeleport(item);
        /// </summary>
        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        private static class InventoryGridIconPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                FieldInfo noteleport = AccessTools.Field(typeof(InventoryElement), nameof(InventoryElement.m_noteleport));
                FieldInfo shared = AccessTools.Field(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.m_shared));
                FieldInfo teleportable = AccessTools.Field(typeof(ItemDrop.ItemData.SharedData),
                    nameof(ItemDrop.ItemData.SharedData.m_teleportable));
                MethodInfo getGlobalKey = AccessTools.Method(typeof(ZoneSystem), nameof(ZoneSystem.GetGlobalKey),
                    new[] { typeof(GlobalKeys) });

                var matcher = new CodeMatcher(instructions).MatchStartForward(
                    new CodeMatch(OpCodes.Ldfld, noteleport),
                    new CodeMatch(i => i.IsLdloc()),    // the item
                    new CodeMatch(OpCodes.Ldfld, shared),
                    new CodeMatch(OpCodes.Ldfld, teleportable),
                    new CodeMatch(i => i.opcode == OpCodes.Brtrue || i.opcode == OpCodes.Brtrue_S),
                    new CodeMatch(OpCodes.Call),        // ZoneSystem.instance
                    new CodeMatch(i => i.opcode == OpCodes.Ldc_I4 || i.opcode == OpCodes.Ldc_I4_S),
                    new CodeMatch(OpCodes.Callvirt, getGlobalKey),
                    new CodeMatch(OpCodes.Ldc_I4_0),
                    new CodeMatch(OpCodes.Ceq),
                    new CodeMatch(i => i.opcode == OpCodes.Br || i.opcode == OpCodes.Br_S),
                    new CodeMatch(OpCodes.Ldc_I4_0),
                    new CodeMatch(OpCodes.Callvirt));   // Behaviour.set_enabled

                if (matcher.IsInvalid)
                {
                    Jotunn.Logger.LogWarning("AccurateTooltips: couldn't find the no-teleport icon in the " +
                        "inventory grid (game update?). Slot icons will show vanilla state.");
                    return instructions;
                }

                // Keep "load item", replace the vanilla condition with our call, keep set_enabled.
                matcher
                    .Advance(2)
                    .RemoveInstructions(10)
                    .Insert(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Tooltips), nameof(ShowNoTeleport))));
                return matcher.InstructionEnumeration();
            }
        }
    }
}
