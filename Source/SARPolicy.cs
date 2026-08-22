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
        // The two facings want quite different amounts. Head-on, the bust barely widens the
        // outline and a strong push reads as pointed, so front and back sit low. In profile the
        // bust protrudes properly and needs most of the available push.
        public const float DefaultStrengthFront = 0.45f;
        public const float DefaultStrengthSide = 0.85f;

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
                strength = Mathf.Clamp01(facing == Facing.Side ? ext.strengthSide : ext.strength);
                keepBreasts = ext.keepBreasts;
                return;
            }

            List<string> tags = def.apparel.tags;
            if (tags == null || tags.Count == 0) return;

            if (tags.Contains(TagForceRefit)) { strength = 1f; return; }
            if (tags.Contains(TagNoRefit) || tags.Contains(ThigappeIgnore)) strength = 0f;
        }

        // How strongly this garment follows the bust, 0 to 1.
        public static float Strength(ThingDef def, SARSettings s, Facing facing)
        {
            if (def == null || s == null) return 0f;

            Dictionary<string, float> store = Store(s, facing);
            if (store.TryGetValue(def.defName, out float user)) return Mathf.Clamp01(user);

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

        public static void SetStrength(ThingDef def, SARSettings s, Facing facing, float value)
        {
            Defaults(def, facing, out float d, out _);
            value = Mathf.Clamp01(value);

            Dictionary<string, float> store = Store(s, facing);
            if (Mathf.Abs(value - d) < 0.001f) store.Remove(def.defName);
            else store[def.defName] = value;
        }

        // Both facings at once, for switching a garment off.
        public static void SetStrengthAll(ThingDef def, SARSettings s, float value)
        {
            SetStrength(def, s, Facing.Front, value);
            SetStrength(def, s, Facing.Side, value);
        }

        // Restores each facing to its own default. Not the same as setting one value on both,
        // because front and side differ.
        public static void RestoreDefaults(ThingDef def, SARSettings s)
        {
            SetStrength(def, s, Facing.Front, DefaultStrengthFront);
            SetStrength(def, s, Facing.Side, DefaultStrengthSide);
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
