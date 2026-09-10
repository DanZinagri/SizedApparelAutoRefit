using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SizedApparelAutoRefit
{
    // Compatibility with Jikulopo Texture Cache (jikulopo.textureCache).
    public static class SARTexCacheCompat
    {
        private static bool resolved;
        private static FieldInfo texturesField;
        private static MethodInfo clearMethod;

        public static bool Present
        {
            get
            {
                Resolve();
                return texturesField != null || clearMethod != null;
            }
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            try
            {
                Type t = AccessTools.TypeByName("texture_cache.TexturesCache");
                if (t == null) return;
                texturesField = AccessTools.Field(t, "textures");
                clearMethod = AccessTools.Method(t, "ClearCache");
                Log.Message("[SA Auto-Refit] Jikulopo Texture Cache detected; its entries for generated "
                            + "textures will be expired on regeneration.");
            }
            catch (Exception ex)
            {
                Log.Warning("[SA Auto-Refit] Texture cache compat failed to bind: " + ex.Message);
            }
        }

        // Drop the memoised entries for the given paths. Falls back to clearing the whole cache
        // if the private dictionary cannot be reached; that only costs a lazy rebuild.
        public static void Forget(List<string> paths)
        {
            if (!Present || paths == null || paths.Count == 0) return;
            try
            {
                if (texturesField?.GetValue(null) is Dictionary<string, Texture2D> dict)
                {
                    for (int i = 0; i < paths.Count; i++) dict.Remove(paths[i]);
                    return;
                }
                clearMethod?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Log.WarningOnce("[SA Auto-Refit] Could not expire texture cache entries: " + ex.Message, 2048033117);
            }
        }
    }
}
