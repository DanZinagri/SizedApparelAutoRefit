using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SizedApparelAutoRefit
{
    // Per-item override for modders, or for a user patching a garment.
    //
    // <li Class="SizedApparelAutoRefit.SARFitExtension">
    //   <strength>0.0</strength>
    //   <keepBreasts>true</keepBreasts>
    // </li>
    public class SARFitExtension : DefModExtension
    {
        public float strength = SARPolicy.DefaultStrengthFront;      // front and back
        public float strengthSide = SARPolicy.DefaultStrengthSide;   // east and west
        public bool keepBreasts = true;
    }

    // What we do to one garment, as two independent decisions.
    //
    // Keep breasts registers a sized variant so Sized Apparel's pawn-wide
    // unsupported-apparel check passes and body parts keep rendering. Refit reshapes the
    // texture to follow the bust. They are deliberately separate: a small barbarian harness
    // needs no reshaping but should still let the exposed chest draw, while heavy armour that
    // breasts poke out from behind needs the opposite.
    //
    // Both default to on. Earlier versions tried to infer this from apparel layer, armour
    // stats and measured drape; each rule was right for most garments and confidently wrong for
    // some, and a wrong guess is invisible until you look at the pawn. The list in mod settings
    // is the authority instead.
    public static class SARPolicy
    {
        // The slider still reaches past 1.0 so a build can be pushed beyond correct coverage if
        // wanted, but 1.0 is the calibrated target rather than an arbitrary amount.
        public const float MaxStrength = 2f;

        // Both 1.0. Strength is measured against the garment's own edge and then corrected per
        // build by SARCalibration, so 1.0 means "correctly covered" on every build and facing.
        // The old split values compensated for a displacement that was wrong by a different
        // amount in each view; that is handled properly now.
        public const float DefaultStrengthFront = 1f;
        public const float DefaultStrengthSide = 1f;

        public static float DefaultFor(Facing facing)
        {
            return facing == Facing.Side ? DefaultStrengthSide : DefaultStrengthFront;
        }

        public const string TagNoRefit = "SAR_NoRefit";
        public const string TagForceRefit = "SAR_ForceRefit";
        private const string ThigappeIgnore = "THIGAPPE_IgnoreTag";

        // Front and side are separate because the bust protrudes much further in profile than
        // head-on, so a value that looks right from the front is usually far too weak from the
        // side, and vice versa.
        public enum Facing { Front, Side }

        // Defaults from tags and mod extensions, before any user choice.
        public static void Defaults(ThingDef def, out float strength, out bool keepBreasts)
        {
            Defaults(def, Facing.Front, out strength, out keepBreasts);
        }

        public static void Defaults(ThingDef def, Facing facing, out float strength, out bool keepBreasts)
        {
            strength = DefaultFor(facing);
            keepBreasts = true;
            if (def?.apparel == null) return;

            SARFitExtension ext = def.GetModExtension<SARFitExtension>();
            if (ext != null)
            {
                strength = Mathf.Clamp(facing == Facing.Side ? ext.strengthSide : ext.strength, 0f, MaxStrength);
                keepBreasts = ext.keepBreasts;
                return;
            }

            List<string> tags = def.apparel.tags;
            if (tags == null || tags.Count == 0) return;

            if (tags.Contains(TagForceRefit)) { strength = 1f; return; }
            if (tags.Contains(TagNoRefit) || tags.Contains(ThigappeIgnore)) strength = 0f;
        }

        // How strongly this garment follows the bust, 0 to 1.
        // Settings are per body type: the bust sits in a different place and protrudes by a
        // different amount on each build. Measured on Sized Apparel's own art, the Hulk bust peaks
        // around v=0.29 with a push of 0.045 while Female peaks at v=0.44 with 0.076, so a value
        // tuned on one build is simply wrong on another.
        public static string Key(ThingDef def, string bodyType)
        {
            return def.defName + "|" + bodyType;
        }

        public static float Strength(ThingDef def, SARSettings s, Facing facing, string bodyType)
        {
            if (def == null || s == null) return 0f;

            Dictionary<string, float> store = Store(s, facing);

            if (!bodyType.NullOrEmpty() && store.TryGetValue(Key(def, bodyType), out float perBody))
                return Mathf.Clamp(perBody, 0f, MaxStrength);

            // Settings saved before body types were separated are keyed by defName alone. They
            // still apply to every build until that build is given its own value.
            if (store.TryGetValue(def.defName, out float legacy)) return Mathf.Clamp(legacy, 0f, MaxStrength);

            Defaults(def, facing, out float d, out _);
            return d;
        }

        private static Dictionary<string, float> Store(SARSettings s, Facing facing)
        {
            return facing == Facing.Side ? s.strengthsSide : s.strengths;
        }

        public static bool KeepBreasts(ThingDef def, SARSettings s)
        {
            if (def == null || s == null) return false;
            Defaults(def, out _, out bool keep);
            if (s.breastsOn.Contains(def.defName)) return true;
            if (s.breastsOff.Contains(def.defName)) return false;
            return keep;
        }

        public static void SetStrength(ThingDef def, SARSettings s, Facing facing, string bodyType, float value)
        {
            Defaults(def, facing, out float d, out _);
            value = Mathf.Clamp(value, 0f, MaxStrength);

            Dictionary<string, float> store = Store(s, facing);
            string key = Key(def, bodyType);

            // Writing a per-build value retires the legacy shared entry for this garment, so the
            // build being edited is no longer shadowed by it.
            bool hadLegacy = store.ContainsKey(def.defName);

            if (Mathf.Abs(value - d) < 0.001f && !hadLegacy) store.Remove(key);
            else store[key] = value;

            if (hadLegacy)
            {
                float shared = store[def.defName];
                store.Remove(def.defName);

                // Preserve the old shared value on every other build, so separating one build
                // does not silently reset the rest.
                foreach (BodyTypeDef other in SARBodyTypes.Eligible())
                {
                    if (other.defName == bodyType) continue;
                    string otherKey = Key(def, other.defName);
                    if (!store.ContainsKey(otherKey)) store[otherKey] = shared;
                }
            }
        }

        // Both facings at once, for switching a garment off.
        public static void SetStrengthAll(ThingDef def, SARSettings s, string bodyType, float value)
        {
            SetStrength(def, s, Facing.Front, bodyType, value);
            SetStrength(def, s, Facing.Side, bodyType, value);
        }

        // Restores each facing to its own default. Not the same as setting one value on both,
        // because front and side differ.
        public static void RestoreDefaults(ThingDef def, SARSettings s, string bodyType)
        {
            SetStrength(def, s, Facing.Front, bodyType, DefaultStrengthFront);
            SetStrength(def, s, Facing.Side, bodyType, DefaultStrengthSide);
        }

        public static void SetKeepBreasts(ThingDef def, SARSettings s, bool value)
        {
            Defaults(def, out _, out bool def0);
            s.breastsOn.Remove(def.defName);
            s.breastsOff.Remove(def.defName);
            if (value != def0) (value ? s.breastsOn : s.breastsOff).Add(def.defName);
        }
    }
}
