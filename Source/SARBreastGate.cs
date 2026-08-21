using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SizedApparelAutoRefit
{
    // Makes the per-garment "draw breasts" switch actually decide something.
    //
    // Registering a sized texture path is enough for garments that have one, but apparel drawing
    // through its own render nodes has no path of its own — Sized Apparel composes the identical
    // string for every such garment, so a registration can never distinguish them. Patching the
    // draw decision itself does, and it applies uniformly to every garment rather than only the
    // ones with a worn texture.
    //
    // The switch decides in both directions. Turning it off has to be able to override Sized
    // Apparel saying yes, because registering the sized path already cleared the flag that
    // would otherwise have hidden body parts — so an off switch that only declined to force
    // "on" would do nothing at all.
    [HarmonyPatch]
    internal static class Patch_SA_CanDrawBreasts
    {
        private static MethodBase target;

        private static bool Prepare()
        {
            System.Type t = AccessTools.TypeByName("SizedApparel.SizedApparelUtility");
            if (t == null) return false;
            target = AccessTools.Method(t, "CanDrawBreasts");
            if (target == null)
            {
                Log.Warning("[SA Auto-Refit] SizedApparelUtility.CanDrawBreasts did not resolve; "
                            + "the per-garment 'draw breasts' switch will not apply.");
                return false;
            }
            return true;
        }

        private static MethodBase TargetMethod() => target;

        private static void Postfix(Pawn pawn, ref bool __result)
        {
            SARSettings s = SARMod.Settings;
            if (s == null || !s.enabled) return;
            if (pawn?.apparel?.WornApparel == null) return;

            List<Apparel> worn = pawn.apparel.WornApparel;
            bool anyChest = false;

            for (int i = 0; i < worn.Count; i++)
            {
                ThingDef def = worn[i]?.def;
                if (def?.apparel == null) continue;
                if (!CoversChest(def)) continue;

                anyChest = true;

                // One garment switched off hides body parts, matching how Sized Apparel already
                // treats an unsupported garment: the decision is pawn-wide, not per layer.
                if (!SARPolicy.KeepBreasts(def, s))
                {
                    __result = false;
                    return;
                }
            }

            if (anyChest) __result = true;
        }

        private static bool CoversChest(ThingDef def)
        {
            List<BodyPartGroupDef> groups = def.apparel.bodyPartGroups;
            if (groups == null) return false;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] == BodyPartGroupDefOf.Torso) return true;
                if (groups[i] != null && groups[i].defName == "Chest") return true;
            }
            return false;
        }
    }
}
