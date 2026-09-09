using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace SizedApparelAutoRefit
{
    // All coupling to Sized Apparel for RJW lives here, and it is reflection-only.
    // We deliberately do not reference its assembly: it hard-depends on RJW, and its
    // internals move between releases. Every lookup is null-checked so that a drifted
    // API degrades into "do nothing" rather than a stack of red errors.
    public static class SABridge
    {
        private static bool resolved;
        private static bool broken;

        private static FieldInfo textureExistsSetField;
        private static FieldInfo useGenderSpecificField;
        private static FieldInfo useBodyTextureField;
        private static MethodInfo clearAllMethod;
        private static MethodInfo textureExistsMethod;

        public static bool Active
        {
            get
            {
                Resolve();
                return !broken && textureExistsSetField != null;
            }
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            try
            {
                Type util = AccessTools.TypeByName("SizedApparel.SizedApparelUtility");
                if (util == null)
                {
                    // Sized Apparel simply is not installed. Not an error.
                    broken = true;
                    return;
                }

                textureExistsSetField = AccessTools.Field(util, "textureExistsSet");
                if (textureExistsSetField == null)
                {
                    Log.Warning("[SA Auto-Refit] Found Sized Apparel but SizedApparelUtility.textureExistsSet did not resolve. "
                                + "Its API has drifted; auto-refit is disabled.");
                    broken = true;
                    return;
                }

                Type settings = AccessTools.TypeByName("SizedApparel.SizedApparelSettings");
                if (settings != null)
                {
                    useGenderSpecificField = AccessTools.Field(settings, "useGenderSpecificTexture");
                    useBodyTextureField = AccessTools.Field(settings, "useBodyTexture");
                }

                textureExistsMethod = AccessTools.Method(util, "TextureExists", new[] { typeof(string) });

                Type db = AccessTools.TypeByName("SizedApparel.SizedApparelsDatabase");
                if (db != null) clearAllMethod = AccessTools.Method(db, "ClearAll");
            }
            catch (Exception ex)
            {
                broken = true;
                Log.Warning("[SA Auto-Refit] Failed to bind to Sized Apparel: " + ex.Message);
            }
        }

        // Sized Apparel's texture-existence set.
        //
        // This is the single gate every apparel lookup passes through. Reading the field
        // forces its static initializer to run first, so the snapshot it builds from the
        // running mods' content lists is already complete by the time we add to it.
        //
        // Crucially this works whether or not SA's lazy texture loading is enabled:
        // TextureExists() only takes its lazy branch for paths present in lazyTextureSet
        // (real files SA stripped out of the content list), and always falls through to
        // "return textureExistsSet.Contains(path)". Our synthetic paths are never files,
        // so they are never in lazyTextureSet and both modes reach the same check.
        public static HashSet<string> TextureExistsSet
        {
            get
            {
                if (!Active) return null;
                try
                {
                    return textureExistsSetField.GetValue(null) as HashSet<string>;
                }
                catch (Exception ex)
                {
                    broken = true;
                    Log.Warning("[SA Auto-Refit] Could not read textureExistsSet: " + ex.Message);
                    return null;
                }
            }
        }

        public static bool UseGenderSpecificTexture
        {
            get
            {
                if (!Active || useGenderSpecificField == null) return true;
                try { return (bool)useGenderSpecificField.GetValue(null); }
                catch { return true; }
            }
        }

        // When SA's own body textures are off it renders the vanilla body, so its drawn
        // silhouette is the vanilla one and there is nothing for us to refit against.
        public static bool UseBodyTexture
        {
            get
            {
                if (!Active || useBodyTextureField == null) return true;
                try { return (bool)useBodyTextureField.GetValue(null); }
                catch { return true; }
            }
        }

        // Drops SA's resolved-path caches.
        //
        // This matters more than it looks. GetSupportedApparelSizedPath negative-caches
        // hard: on a miss it writes a "failed" result for all 11 size indices of that key.
        // If anything probed a path before we registered it, that key stays poisoned for
        // the session unless we clear it.
        // Sized Apparel's own existence check. Unlike ContentFinder this understands SA's lazy
        // texture loading: with that setting on — and it is on by default — every path under a
        // "SizedApparel" folder is stripped from the content lists at load and only materialised
        // when SA itself asks for it. Asking ContentFinder directly for SA's breast or body-part
        // art then returns null, which made this mod conclude no build had any breast art and
        // register nothing: the apparel window showed "0 garments" on every pawn. Calling this
        // loads the texture as a side effect, after which ContentFinder can see it.
        public static bool TextureExists(string path)
        {
            if (!Active || textureExistsMethod == null || path.NullOrEmpty()) return false;
            try { return (bool)textureExistsMethod.Invoke(null, new object[] { path }); }
            catch (Exception ex)
            {
                Log.WarningOnce("[SA Auto-Refit] SizedApparelUtility.TextureExists threw: " + ex.Message, 2048029917);
                return false;
            }
        }

        public static void ClearResolvedPathCaches()
        {
            if (!Active || clearAllMethod == null) return;
            try { clearAllMethod.Invoke(null, null); }
            catch (Exception ex) { Log.Warning("[SA Auto-Refit] ClearAll failed: " + ex.Message); }
        }
    }
}
