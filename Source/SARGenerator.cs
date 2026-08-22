using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SizedApparelAutoRefit
{
    // Builds a sized apparel texture on demand by stretching the vanilla garment to follow
    // the outline Sized Apparel draws at that size stage.
    //
    // The warp is per-scanline and horizontal only. For each row we know how wide the bare
    // body is (the reference the garment was drawn against) and how wide body-plus-breasts
    // is (what the pawn actually shows), so the destination row is mapped back onto the
    // reference row and resampled. Left and right are mapped independently about the body
    // centreline, which is what makes the side views behave: breasts push the outline
    // forward only, so the back edge maps identity and does not smear.
    //
    // Finished textures are injected into this mod's own content holder, which is where
    // ContentFinder looks. That is the same handoff Sized Apparel's lazy texture loader
    // uses, and it avoids hooking ContentFinder itself — see SARPatches for why that
    // matters.
    public static class SARGenerator
    {
        private static readonly HashSet<string> ensured = new HashSet<string>();
        private static readonly List<string> injectedPaths = new List<string>();

        // Textures we actually allocated, as opposed to borrowed references to art that
        // already exists. Only these consume memory, so only these count against the cap.
        private static int ownedCount;
        private static readonly Dictionary<string, SARSilhouette> silhouettes = new Dictionary<string, SARSilhouette>();

        private static bool capWarned;

        public static int GeneratedCount => injectedPaths.Count;

        private static readonly string[] Rotations = { "south", "north", "east" };

        // Ignore silhouette growth narrower than this (fraction of texture width, ~4px at
        // 512) so antialiased edges and stray pixels do not trigger a stretch. Measured
        // against Sized Apparel's own hand-drawn ShirtBasic variants: it slightly improves
        // the smallest sizes and costs nothing at the large ones.
        private const float GrowthDeadzone = 0.008f;

        // Outside the body outline, the outward push decays to nothing over this multiple of
        // the push itself. A bulge perturbs nearby cloth, not the whole scanline — without
        // this, sleeves and drape far from the centreline get thrown outward in proportion to
        // their distance, which reads as splayed spikes rather than a fitted garment.
        private const float FalloffFactor = 2f;

        // Fades a push out below the deadzone instead of snapping it to zero. A hard cutoff puts
        // a step in the outline exactly where the displacement crosses the threshold.
        private static float Deadzone(float push)
        {
            if (push <= 0f) return 0f;
            if (push >= GrowthDeadzone * 2f) return push;
            if (push <= GrowthDeadzone) return 0f;
            float t = (push - GrowthDeadzone) / GrowthDeadzone;
            return push * Mathf.SmoothStep(0f, 1f, t);
        }

        // Horizontal subsamples per destination pixel. The warp compresses the source where the
        // garment is pulled outward, and a single sample per pixel there aliases the outline.
        // Averaging in premultiplied alpha keeps the edge smooth.
        private const int SuperSamples = 3;


        private static readonly Dictionary<string, GarmentSpan> garmentSpans = new Dictionary<string, GarmentSpan>();


        // A garment's own per-row outline, in the same normalised space as the body.
        private class GarmentSpan
        {
            public float[] left = new float[SARSilhouette.Rows];
            public float[] right = new float[SARSilhouette.Rows];
            public bool valid;
        }

        // Called just before a graphic is built. If the requested path is one of ours,
        // make sure every rotation exists in the content holder first, so the ordinary
        // ContentFinder lookups inside Graphic.Init simply succeed.
        //
        // Runs for every graphic request in the game, so the rejection path is two hash
        // lookups and nothing else.
        public static void EnsureFor(string basePath)
        {
            if (basePath.NullOrEmpty()) return;
            if (!SARRegistry.Ready) return;
            if (ensured.Contains(basePath)) return;
            if (!SARRegistry.TryGet(basePath, out SARKey key)) return;

            // RenderTexture readback is main-thread only. GetInner refuses off-thread graphic
            // loads anyway, so bailing here just lets that existing guard report it.
            if (!UnityData.IsInMainThread) return;

            ensured.Add(basePath);

            SARSettings s = SARMod.Settings;
            if (s == null || !s.enabled) return;

            Dictionary<string, Texture2D> holder = ContentHolder();
            if (holder == null) return;

            if (ownedCount >= s.maxCachedTextures)
            {
                if (!capWarned)
                {
                    capWarned = true;
                    Log.Warning($"[SA Auto-Refit] Hit the generated-texture cap ({s.maxCachedTextures}). "
                                + "Further garments keep their vanilla shape. Raise the cap in mod settings if you have the VRAM.");
                }
                return;
            }

            try
            {
                for (int i = 0; i < Rotations.Length; i++)
                {
                    Inject(holder, key, Rotations[i], isMask: false);
                    Inject(holder, key, Rotations[i], isMask: true);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[SA Auto-Refit] Failed generating " + basePath + ": " + ex, basePath.GetHashCode());
            }
        }

        private static void Inject(Dictionary<string, Texture2D> holder, SARKey key, string rot, bool isMask)
        {
            string suffix = "_" + rot + (isMask ? "m" : "");
            string destPath = SARRegistry.BasePathFor(key) + suffix;
            if (holder.ContainsKey(destPath)) return;

            Texture2D built = Build(key, rot, isMask, out bool borrowed);
            if (built == null) return;   // no source art for this rotation; vanilla fallback handles it

            if (!borrowed)
            {
                // Never rename a borrowed texture: it belongs to whichever mod supplied the
                // original art and is still being drawn under its own path.
                built.name = "SAR_" + destPath;
                ownedCount++;
            }

            holder[destPath] = built;
            injectedPaths.Add(destPath);
        }

        // This mod's own texture content list. ContentFinder walks the running mods in
        // reverse load order, and we load after Sized Apparel, so entries here are found.
        // Only paths that exist nowhere else are ever added, so nothing is shadowed.
        private static Dictionary<string, Texture2D> ContentHolder()
        {
            ModContentPack content = LoadedModManager.GetMod<SARMod>()?.Content;
            return content?.GetContentHolder<Texture2D>()?.contentList;
        }

        // Produces the texture for one synthetic path.
        //
        // borrowed is set when the result is a shared reference to art that
        // already exists rather than something we allocated. That is the normal outcome for
        // garments with zero fit strength: the variant still has to exist, because Sized
        // Apparel gates a pawn-wide flag on its existence, but it is byte-identical to the
        // source and there is no reason to spend a megabyte duplicating it.
        private static Texture2D Build(SARKey key, string rot, bool isMask, out bool borrowed)
        {
            borrowed = false;

            string srcPath = key.SourceBase + "_" + rot + (isMask ? "m" : "");

            BodyTypeDef bt = DefDatabase<BodyTypeDef>.GetNamedSilentFail(key.bodyType);
            if (bt == null) return null;

            // Render-node apparel registers a shared path purely to satisfy Sized Apparel's
            // pawn-wide check; there is no worn texture behind it. Nothing should ever draw this,
            // but hand back a transparent pixel rather than nothing so a stray request cannot
            // produce a missing-texture error.
            if (key.wornPath.NullOrEmpty())
            {
                Texture2D blank = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                blank.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                blank.Apply(false, false);
                return blank;
            }

            ThingDef def = SARRegistry.DefFor(key.wornPath);
            string how = "no def";
            float fit = def == null ? 0f : ResolveFit(def, rot, out how);

            LogVerdict(key, rot, how, fit);

            // Nothing to reshape for this garment: hand back the original texture itself.
            if (fit <= 0.001f)
            {
                Texture2D raw = ContentFinder<Texture2D>.Get(srcPath, false);
                if (raw == null) return null;
                borrowed = true;
                return raw;
            }

            Texture2D src = SARTex.LoadReadable(srcPath);
            if (src == null) return null;

            SARSilhouette sil = GetSilhouette(bt, key.maleSuffix, key.size, rot);

            // No usable outline for this rotation: hand back the source unchanged rather than
            // null, so the graphic keeps a complete rotation set instead of having
            // Graphic_Multi substitute the south texture for a missing north.
            if (sil == null || !sil.valid || !sil.hasBand)
            {
                Texture2D raw = ContentFinder<Texture2D>.Get(srcPath, false);
                if (raw != null) { borrowed = true; return raw; }
                return Copy(src);
            }

            GarmentSpan span = SpanFor(key, rot, src);
            if (span == null || !span.valid) return Copy(src);

            return Warp(src, sil, span, SARMod.Settings, fit, key, rot);
        }

        // The garment's own outline for this rotation, measured from the very texture we are
        // about to warp. This is the reference the refit displaces.
        private static GarmentSpan SpanFor(SARKey key, string rot, Texture2D src)
        {
            string cacheKey = key.wornPath + "|" + key.bodyType + "|" + key.maleSuffix + "|" + rot;
            if (garmentSpans.TryGetValue(cacheKey, out GarmentSpan cached)) return cached;

            GarmentSpan span = new GarmentSpan();
            span.valid = SARSilhouette.SpansFor(src, span.left, span.right);

            garmentSpans[cacheKey] = span;
            return span;
        }

        private static SARSilhouette GetSilhouette(BodyTypeDef bt, bool male, int size, string rot)
        {
            string cacheKey = bt.defName + "|" + male + "|" + size + "|" + rot;
            if (silhouettes.TryGetValue(cacheKey, out SARSilhouette cached)) return cached;

            SARSilhouette built = null;

            string bodyBase = SARPaths.BodySilhouettePath(bt, male);
            if (bodyBase != null)
            {
                Texture2D body = SARTex.LoadReadable(bodyBase + "_" + rot);
                if (body != null)
                {
                    string breastBase = SARPaths.BreastOverlayPath(bt, male, size);
                    Texture2D breast = breastBase == null ? null : SARTex.LoadReadable(breastBase + "_" + rot);
                    built = SARSilhouette.Build(body, breast);
                }
            }

            silhouettes[cacheKey] = built;
            return built;
        }

        // Whether this garment is reshaped, and how strongly. The decision is the user's, taken
        // from the per-item list; the global slider only scales the result.
        // East (and the west it mirrors) is the side profile; south and north are front and back.
        private static float ResolveFit(ThingDef def, string rot, out string how)
        {
            SARSettings s = SARMod.Settings;
            SARPolicy.Facing facing = rot == "east" ? SARPolicy.Facing.Side : SARPolicy.Facing.Front;

            float strength = SARPolicy.Strength(def, s, facing);
            how = $"{(facing == SARPolicy.Facing.Side ? "side" : "front")} strength {strength:0.00}";
            return strength;
        }

        private static readonly HashSet<string> loggedVerdicts = new HashSet<string>();

        // One line per garment explaining what it was classified as and why. Logged once per
        // garment rather than per texture, so the list stays readable enough to scan for
        // anything that guessed wrong.
        private static void LogVerdict(SARKey key, string rot, string how, float fit)
        {
            SARSettings s = SARMod.Settings;
            if (s == null || !s.logGeneration) return;
            if (rot != "south" || !loggedVerdicts.Add(key.wornPath)) return;

            string name = key.wornPath.Substring(key.wornPath.LastIndexOf('/') + 1);
            Log.Message($"[SA Auto-Refit] {name}: fit={fit:0.00} via {how}"
                        + (fit <= 0.001f ? "  (left unchanged)" : ""));
        }

        private static Texture2D Copy(Texture2D src)
        {
            Texture2D dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            dst.SetPixels32(src.GetPixels32());
            dst.Apply(false, false);
            dst.filterMode = src.filterMode;
            dst.wrapMode = TextureWrapMode.Clamp;
            return dst;
        }

        // Displaces the garment's own outline outward by however far the breasts protrude past
        // the bare body, then resamples.
        //
        // The displacement is additive and taken from the body art alone, so it does not matter
        // which body the garment was drawn against. Remapping the body outline onto the garment
        // instead — the previous approach — silently assumed the garment was authored for
        // Sized Apparel's body. Vanilla apparel is drawn for the vanilla body, whose torso is far
        // wider than SA's, which inflated the stretch to well over 2x and splayed loose clothing.
        private static Texture2D Warp(Texture2D src, SARSilhouette sil, GarmentSpan span,
                                      SARSettings s, float fit, SARKey key, string rot)
        {
            int w = src.width;
            int h = src.height;

            Color32[] sp = src.GetPixels32();
            Color32[] dp = new Color32[sp.Length];

            float feather = s?.FeatherV ?? 0.06f;
            float gain = s?.bulgeGain ?? 1f;

            float maxPush = 0f;
            int warpedRows = 0;

            for (int y = 0; y < h; y++)
            {
                int off = y * w;
                float v = (y + 0.5f) / h;
                // Interpolated rather than nearest-row: nearest quantises the displacement into
                // bands one grid row tall, which reads as stair-stepping along a reshaped edge.
                float bodyR = SARSilhouette.Lerp(sil.refRight, v), bodyL = SARSilhouette.Lerp(sil.refLeft, v);
                float bustR = SARSilhouette.Lerp(sil.tgtRight, v), bustL = SARSilhouette.Lerp(sil.tgtLeft, v);

                float scale = sil.BandWeight(v, feather) * gain * fit;
                float pushR = SARSilhouette.Lerp(sil.pushRight, v) * scale;
                float pushL = SARSilhouette.Lerp(sil.pushLeft, v) * scale;

                pushR = Deadzone(pushR);
                pushL = Deadzone(pushL);

                float gl = SARSilhouette.Lerp(span.left, v), gr = SARSilhouette.Lerp(span.right, v);

                if ((pushR <= 0f && pushL <= 0f) || gl < 0f || gr < 0f || (gr - gl) <= 1e-4f)
                {
                    Array.Copy(sp, off, dp, off, w);
                    continue;
                }

                warpedRows++;
                if (pushR > maxPush) maxPush = pushR;
                if (pushL > maxPush) maxPush = pushL;

                // Reference is the garment's own edge; target is that edge pushed outward.
                float cx = (gl + gr) * 0.5f;
                float rightRef = gr - cx;
                float leftRef = cx - gl;
                float rightTgt = rightRef + pushR;
                float leftTgt = leftRef + pushL;

                for (int x = 0; x < w; x++)
                {
                    float accR = 0f, accG = 0f, accB = 0f, accA = 0f;

                    for (int ss = 0; ss < SuperSamples; ss++)
                    {
                    float u = (x + (ss + 0.5f) / SuperSamples) / w;
                    float uSrc;

                    if (u >= cx)
                    {
                        float d = u - cx;
                        if (d <= rightTgt)
                        {
                            uSrc = cx + d / rightTgt * rightRef;
                        }
                        else
                        {
                            float fall = Mathf.Max(pushR * FalloffFactor, 1e-6f);
                            float decay = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((d - rightTgt) / fall));
                            uSrc = u - pushR * decay;
                        }
                    }
                    else
                    {
                        float d = cx - u;
                        if (d <= leftTgt)
                        {
                            uSrc = cx - d / leftTgt * leftRef;
                        }
                        else
                        {
                            float fall = Mathf.Max(pushL * FalloffFactor, 1e-6f);
                            float decay = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((d - leftTgt) / fall));
                            uSrc = u + pushL * decay;
                        }
                    }

                        Color32 c = SARTex.SampleUV(sp, w, h, uSrc, v);
                        // Accumulate premultiplied so partly transparent edge samples do not drag
                        // their colour into the average.
                        float a = c.a;
                        accR += c.r * a; accG += c.g * a; accB += c.b * a; accA += a;
                    }

                    if (accA <= 0.001f) { dp[off + x] = new Color32(0, 0, 0, 0); continue; }

                    float outA = accA / SuperSamples;
                    dp[off + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(accR / accA), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(accG / accA), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(accB / accA), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(outA), 0, 255));
                }
            }

            if (s != null && s.logGeneration)
            {
                Log.Message($"[SA Auto-Refit] {key.wornPath.Split('/')[key.wornPath.Split('/').Length - 1]} "
                            + $"bt={key.bodyType}{(key.maleSuffix ? "M" : "")} size={key.size} rot={rot} "
                            + $"fit={fit:0.00} band=[{sil.bandTopV:0.000},{sil.bandBottomV:0.000}] "
                            + $"rows warped={warpedRows}/{h} maxPush={maxPush:0.0000} ({maxPush * w:0.0}px)");
            }

            Texture2D dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
            dst.SetPixels32(dp);
            dst.Apply(false, false);
            dst.filterMode = src.filterMode;
            dst.wrapMode = TextureWrapMode.Clamp;
            dst.anisoLevel = src.anisoLevel;
            return dst;
        }

        // Withdraws every generated texture so the next request rebuilds it.
        //
        // Textures are removed from the content list but not destroyed: live Materials
        // still reference them, and freeing one out from under a Material renders the
        // garment invisible. Memory is reclaimed on restart.
        public static void ClearCache()
        {
            Dictionary<string, Texture2D> holder = ContentHolder();
            if (holder != null)
            {
                for (int i = 0; i < injectedPaths.Count; i++) holder.Remove(injectedPaths[i]);
            }

            injectedPaths.Clear();
            ownedCount = 0;
            ensured.Clear();
            silhouettes.Clear();
            garmentSpans.Clear();
            loggedVerdicts.Clear();

            capWarned = false;
            SARPaths.ClearCaches();
            SARTex.ClearReadableCache();
        }
    }
}
