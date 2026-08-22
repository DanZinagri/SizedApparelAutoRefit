using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SizedApparelAutoRefit
{
    // Reproduces Sized Apparel's own path composition for the two things we need to read:
    // the body it swaps in, and the breast overlay it draws on top.
    //
    // SA never shadows the vanilla body texture. It takes bodyNakedGraphicPath and appends
    // "_BaseBody" (plus F/M when gender-specific textures are on, plus a variation suffix),
    // then installs that graphic from a postfix on PawnRenderNode_Body.GraphicFor. Reading
    // the def path alone gets you the vanilla body, which is exactly the trap that leaves
    // other refitters fitting clothes to a silhouette nobody is looking at.
    public static class SARPaths
    {
        public const string BreastFolder = "SizedApparel/BodyParts/Human/Breasts/";

        // Highest breast size index Sized Apparel ships art for.
        public const int MaxBreastArtIndex = 6;

        private static readonly Dictionary<string, string> bodyPathCache = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> breastPathCache = new Dictionary<string, string>();

        // Base path (no rotation suffix) of the body art SA actually renders for this build,
        // or null when nothing usable exists.
        public static string BodySilhouettePath(BodyTypeDef bodyType, bool male = false)
        {
            if (bodyType == null) return null;
            string naked = bodyType.bodyNakedGraphicPath;
            if (naked.NullOrEmpty()) return null;

            string cacheKey = naked + "|" + male + "|" + SABridge.UseGenderSpecificTexture;
            if (bodyPathCache.TryGetValue(cacheKey, out string cached)) return cached;

            string result = null;
            foreach (string candidate in BodyCandidates(naked, male))
            {
                if (SARTex.Exists(candidate + "_south")) { result = candidate; break; }
            }

            bodyPathCache[cacheKey] = result;
            return result;
        }

        private static IEnumerable<string> BodyCandidates(string naked, bool male)
        {
            // Only try the gendered variant when Sized Apparel is actually using gendered body
            // textures. Its GetBodyGraphic only appends F/M under that setting, and the Hulk build
            // ships both variants differing by up to 82px in width — measuring against the wrong
            // one puts the whole refit in the wrong place.
            if (SABridge.UseGenderSpecificTexture)
            {
                yield return naked + "_BaseBody" + (male ? "M" : "F");
            }
            yield return naked + "_BaseBody";
            // With SA's body textures disabled it renders the vanilla body, so the vanilla
            // outline is the honest reference.
            yield return naked;
        }

        // Base path of the breast overlay for this size, or null for a flat chest.
        // Art only exists up to index 6; larger sizes clamp down to the biggest available,
        // which is what SA's own fallback search does.
        public static string BreastOverlayPath(BodyTypeDef bodyType, bool male, int size)
        {
            if (bodyType == null || size <= 0) return null;

            string cacheKey = bodyType.defName + "|" + male + "|" + size + "|" + SABridge.UseGenderSpecificTexture;
            if (breastPathCache.TryGetValue(cacheKey, out string cached)) return cached;

            string result = null;
            int start = size > MaxBreastArtIndex ? MaxBreastArtIndex : size;

            for (int n = start; n >= 1 && result == null; n--)
            {
                foreach (string candidate in BreastCandidates(bodyType.defName, male, n))
                {
                    if (SARTex.Exists(candidate + "_south")) { result = candidate; break; }
                }
            }

            breastPathCache[cacheKey] = result;
            return result;
        }

        private static IEnumerable<string> BreastCandidates(string bodyType, bool male, int n)
        {
            string g = male ? "M" : "F";
            bool gendered = SABridge.UseGenderSpecificTexture;

            if (gendered) yield return BreastFolder + "Breasts_" + bodyType + g + "_" + n;
            yield return BreastFolder + "Breasts_" + bodyType + "_" + n;
            if (gendered) yield return BreastFolder + "Breasts" + g + "_" + n;
            yield return BreastFolder + "Breasts_" + n;
        }

        public static void ClearCaches()
        {
            bodyPathCache.Clear();
            breastPathCache.Clear();
        }
    }
}
