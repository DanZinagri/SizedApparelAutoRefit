using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SizedApparelAutoRefit
{
    // Identifies one synthetic apparel texture we are prepared to generate.
    public struct SARKey
    {
        public string wornPath;      // apparel def's wornGraphicPath
        public string bodyType;      // BodyTypeDef defName
        public bool maleSuffix;      // the "M" Sized Apparel appends for male pawns
        public int size;             // breast size index, 0..10

        public SARKey(string wornPath, string bodyType, bool maleSuffix, int size)
        {
            this.wornPath = wornPath;
            this.bodyType = bodyType;
            this.maleSuffix = maleSuffix;
            this.size = size;
        }

        // The vanilla per-body-type texture this garment was drawn as.
        public string SourceBase => wornPath + "_" + bodyType;
    }

    // Declares to Sized Apparel which sized textures exist, without building any of them.
    //
    // Registration is deliberately split from generation: advertising a path is a string
    // concat and a HashSet add, while baking the image is expensive. Sized Apparel decides
    // existence up front (and gates a pawn-wide flag on it), but only ever asks for the
    // handful of textures actually worn.
    public static class SARRegistry
    {
        // Synthetic base path (no rotation suffix) -> what to generate.
        private static readonly Dictionary<string, SARKey> synthetic = new Dictionary<string, SARKey>();

        // wornGraphicPath -> the def it came from.
        private static readonly Dictionary<string, ThingDef> defByWornPath = new Dictionary<string, ThingDef>();

        // Every apparel def we are patching, for the config list.
        private static readonly List<ThingDef> patched = new List<ThingDef>();

        // Apparel this mod covers: chest garments with art, that Sized Apparel does not already
        // ship hand-drawn size variants for.
        public static List<ThingDef> Patched => patched;

        // Paths we added to SA's existence set, so a rebuild can withdraw them.
        private static readonly List<string> advertised = new List<string>();

        // Whether this garment's texture can be reshaped at all. Apparel that draws through its
        // own render nodes has no worn graphic path for us to bake against — Big and Small's
        // armour sets do this — so only the breasts half applies to it.
        public static bool CanRefit(ThingDef def)
        {
            return def?.apparel != null && !def.apparel.wornGraphicPath.NullOrEmpty();
        }

        // The apparel def a synthetic path came from, for classification at generate time.
        public static ThingDef DefFor(string wornPath)
        {
            if (wornPath == null) return null;
            return defByWornPath.TryGetValue(wornPath, out ThingDef d) ? d : null;
        }

        public static bool Ready { get; private set; }
        public static int RegisteredCount => synthetic.Count;
        public static int ApparelCount { get; private set; }

        public const int MaxSize = 10;

        // Path exactly as CheckSizedApparelGraphicExists composes it, minus the race
        // segment. SA falls back to the race-less form for every race, so one registration
        // covers humans and alien races alike.
        public static string BasePathFor(SARKey k)
        {
            return k.wornPath + "_" + k.bodyType + (k.maleSuffix ? "M" : "") + "_" + k.size;
        }

        public static bool TryGet(string basePath, out SARKey key)
        {
            return synthetic.TryGetValue(basePath, out key);
        }

        public static void Build()
        {
            synthetic.Clear();
            defByWornPath.Clear();
            patched.Clear();

            // Withdraw anything a previous build advertised, so toggling "keep breasts" off
            // actually takes effect without a restart.
            HashSet<string> prior = SABridge.TextureExistsSet;
            if (prior != null)
            {
                for (int i = 0; i < advertised.Count; i++) prior.Remove(advertised[i]);
            }
            advertised.Clear();
            Ready = false;
            ApparelCount = 0;

            if (!SABridge.Active) return;

            HashSet<string> existing = SABridge.TextureExistsSet;
            if (existing == null) return;

            SARSettings s = SARMod.Settings;
            if (s == null || !s.enabled) return;

            List<BodyTypeDef> bodyTypes = SARBodyTypes.Eligible();
            if (bodyTypes.Count == 0)
            {
                Log.Warning("[SA Auto-Refit] No body build has both body art and breast art visible, so nothing "
                            + "was registered and the apparel list will be empty. Sized Apparel's breast art lives "
                            + "under SizedApparel/BodyParts/Human/Breasts; if that folder is missing or replaced, "
                            + "check the mod that changed it.");
                return;
            }

            bool registerMale = SABridge.UseGenderSpecificTexture;

            List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (!IsRefittableApparel(def)) continue;

                try
                {

                    // Normalised to "" up front: render-node apparel leaves this null, and a null
                    // key throws in Dictionary. Left unnormalised it aborted the whole registration
                    // at the first such garment, so Ready stayed false and nothing was ever
                    // generated — every garment rendered its vanilla shape no matter what its
                    // strength was set to.
                    string worn = def.apparel.wornGraphicPath ?? "";

                    // The candidate list is what the config window shows, so it must include
                    // garments whose registration is currently switched off — otherwise turning
                    // "draw breasts" off would make the item vanish and become untoggleable.
                    if (!defByWornPath.ContainsKey(worn)) defByWornPath[worn] = def;
                    if (!patched.Contains(def)) patched.Add(def);

                    // Registration is what "draw breasts" actually controls. A registered variant is
                    // what makes Sized Apparel's pawn-wide unsupported-apparel check pass, so body
                    // parts keep rendering under this garment. Whether the garment is also reshaped
                    // is a separate decision, taken at generate time.
                    if (!SARPolicy.KeepBreasts(def, s)) continue;

                    bool used = false;

                    // Sized Apparel's gate composes WornGraphicPath + "_" + bodyType. When the path is
                    // null that is literally "_Female", the same string for every render-node garment,
                    // so one registration covers them all — per-item control is not possible here
                    // because SA cannot tell them apart either.
                    if (worn.NullOrEmpty())
                    {
                        RegisterFor("", bodyTypes, registerMale, existing, ref used);
                }
                    else
                    {
                        RegisterFor(worn, bodyTypes, registerMale, existing, ref used);
                }

                    if (used) ApparelCount++;
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[SA Auto-Refit] Skipped {def.defName} during registration: {ex.Message}");
                }
            }

            // Stable alphabetical order for the config list.
            patched.Sort((a, b) => string.Compare(a.label ?? a.defName, b.label ?? b.defName,
                System.StringComparison.OrdinalIgnoreCase));

            Ready = synthetic.Count > 0;

            // Anything that probed these paths before now got a negative result cached
            // across all 11 sizes. Clear it or the registration is invisible this session.
            SABridge.ClearResolvedPathCaches();

            if (!Ready)
            {
                Log.Warning("[SA Auto-Refit] Registration produced no sized variants, so nothing will be "
                            + "reshaped. Check the warnings above for defs that were skipped.");
                return;
            }

            Log.Message($"[SA Auto-Refit] Registered {synthetic.Count} synthetic sized textures "
                        + $"across {ApparelCount} apparel defs and {bodyTypes.Count} body types.");
        }

        // Advertises every size variant for one worn path.
        private static void RegisterFor(string worn, List<BodyTypeDef> bodyTypes, bool registerMale,
                                        HashSet<string> existing, ref bool used)
        {
            for (int b = 0; b < bodyTypes.Count; b++)
            {
                string bt = bodyTypes[b].defName;

                // A real garment must have art for this build to be worth advertising. Render-node
                // apparel has no worn texture at all, and registers purely to satisfy the gate.
                if (!worn.NullOrEmpty() && !SARTex.Exists(worn + "_" + bt + "_south")) continue;

                for (int g = 0; g < (registerMale ? 2 : 1); g++)
                {
                    bool male = g == 1;

                    // Size 0 (flat chest) is registered too. It reshapes nothing, but its existence
                    // is what SA's pawn-wide check looks for, and a borrowed texture costs nothing.
                    for (int size = 0; size <= MaxSize; size++)
                    {
                        SARKey key = new SARKey(worn, bt, male, size);
                        string basePath = BasePathFor(key);

                        // Hand-drawn art always wins: never shadow a variant SA already ships.
                        if (existing.Contains(basePath + "_south")) continue;
                        if (synthetic.ContainsKey(basePath)) continue;

                        synthetic[basePath] = key;
                        existing.Add(basePath + "_south");
                        advertised.Add(basePath + "_south");
                        used = true;
                    }
                }
            }
        }

        private static bool IsRefittableApparel(ThingDef def)
        {
            if (def?.apparel == null) return false;
            if (!def.IsApparel) return false;

            string worn = def.apparel.wornGraphicPath;

            // A null worn path is not a disqualifier. Apparel that renders through its own
            // render nodes (Big and Small's armour sets) still trips Sized Apparel's pawn-wide
            // check, so it still needs a registered variant even though we cannot reshape it.
            if (!worn.NullOrEmpty())
            {
                if (worn == BaseContent.PlaceholderImagePath) return false;
                if (worn == BaseContent.PlaceholderGearImagePath) return false;
            }

            ApparelLayerDef last = def.apparel.LastLayer;
            if (last == ApparelLayerDefOf.Overhead || last == ApparelLayerDefOf.EyeCover) return false;

            // Only chest coverage matters: that is what SA sizes, and what its
            // hasUnsupportedApparel check inspects.
            List<BodyPartGroupDef> groups = def.apparel.bodyPartGroups;
            if (groups == null) return false;

            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] == BodyPartGroupDefOf.Torso) return true;
                if (groups[i].defName == "Chest") return true;
            }
            return false;
        }

        public static void Clear()
        {
            synthetic.Clear();
            Ready = false;
            ApparelCount = 0;
        }
    }

    public static class SARBodyTypes
    {
        // Body types SA can actually give us a silhouette for — that is, ones with both
        // _BaseBody art and breast overlays. Anything else has nothing to refit against.
        //
        // The breast-art requirement is the real gate. Without it the warp has no target
        // outline and would emit an identity copy, while still telling Sized Apparel the
        // variant is supported — which would clear its unsupported-apparel flag on a body
        // type it cannot actually size.
        public static List<BodyTypeDef> Eligible()
        {
            List<BodyTypeDef> result = new List<BodyTypeDef>();
            SARSettings s = SARMod.Settings;

            foreach (BodyTypeDef bt in DefDatabase<BodyTypeDef>.AllDefsListForReading)
            {
                if (bt?.defName == null) continue;
                if (bt == BodyTypeDefOf.Baby || bt == BodyTypeDefOf.Child) continue;
                if (s != null && !s.BodyTypeEnabled(bt.defName)) continue;
                if (SARPaths.BodySilhouettePath(bt) == null) continue;

                bool hasBreastArt = SARPaths.BreastOverlayPath(bt, false, 1) != null
                                    || SARPaths.BreastOverlayPath(bt, true, 1) != null;
                if (!hasBreastArt) continue;

                result.Add(bt);
            }
            return result;
        }
    }
}
