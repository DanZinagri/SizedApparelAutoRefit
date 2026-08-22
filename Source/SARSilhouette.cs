using UnityEngine;

namespace SizedApparelAutoRefit
{
    // The outline of what Sized Apparel actually draws, sampled row by row.
    //
    // Reference = the bare _BaseBody art (what the vanilla garment was drawn against).
    // Target    = that same body unioned with the breast overlay for this size stage.
    //
    // Rows are resampled onto a fixed grid so that body art, breast art and the apparel
    // texture can differ in resolution without any of the callers caring. Horizontal
    // extents stay at the source texture's native precision.
    public class SARSilhouette
    {
        public const int Rows = 512;
        private const byte AlphaThreshold = 25;

        public readonly float[] refLeft = new float[Rows];
        public readonly float[] refRight = new float[Rows];
        public readonly float[] tgtLeft = new float[Rows];
        public readonly float[] tgtRight = new float[Rows];

        // The breast overlay's own outline. This is what the garment has to clear.
        //
        // An earlier version stored "breast minus body" here and added it to the garment's edge.
        // That mixes baselines: the displacement was measured against the body but applied to the
        // garment, so the body's own taper — wide arms, narrow waist — drove the shape instead of
        // the breast. On the Hulk build it put the widest part of the bulge about 46px below where
        // the breast actually is widest, and on Fat in side view it could never reach far enough
        // to cover the breast at all, at any strength.
        public readonly float[] breastLeft = new float[Rows];
        public readonly float[] breastRight = new float[Rows];

        // How far the breast bulges out from its own attachment to the chest, measured against
        // the straight line joining the top and bottom of the breast art rather than against the
        // body. The body's outline is no use for this: its arms and waist taper independently of
        // the breast and drag the peak away from where the breast actually is widest. Taken from
        // the breast art alone, the peak lands correctly by construction.
        //
        // This is what makes cloth follow the bust at ordinary sizes. Coverage alone — pushing
        // only where the breast escapes the garment — does nothing until the breast is large
        // enough to poke out, so mid-range sizes were left completely flat.
        public readonly float[] bulgeLeft = new float[Rows];
        public readonly float[] bulgeRight = new float[Rows];

        // Vertical extent of the breast overlay, in normalised v. -1 when absent.
        public float bandTopV = -1f;
        public float bandBottomV = -1f;

        public bool valid;
        public bool hasBand;

        // In a side view the breast is drawn on one side of the pawn only — the front. Both edges
        // of the breast blob still bulge outward in the texture, so treating left and right
        // independently pushed the garment out at the pawn's back as well. This records which
        // side actually protrudes past the body, so the other can be left alone.
        public bool frontIsRight = true;

        // Per-row alpha extents of any texture, on the same Rows grid, in normalised u.
        // Single pass over the pixels — used to measure how far a garment's own outline sits
        // outside the body's. Returns false if the texture is unreadable.
        public static bool SpansFor(Texture2D tex, float[] left, float[] right)
        {
            if (tex == null || left == null || right == null) return false;

            Color32[] px = tex.GetPixels32();
            int w = tex.width, h = tex.height;

            for (int i = 0; i < Rows; i++)
            {
                float v = (i + 0.5f) / Rows;
                RowSpan(px, w, h, v, out left[i], out right[i]);
            }
            // Smooth the outline before anything anchors to it. A garment's own edge is notched
            // at arms, belts and folds — most visibly in side view — and anchoring the warp to a
            // jagged edge makes the reshaped result lumpy. Lighter than the push blur: this is
            // real shape, not a profile we are inventing.
            Smooth(left, GarmentBlurRadius);
            Smooth(right, GarmentBlurRadius);
            return true;
        }

        private const int GarmentBlurRadius = 3;

        // Blur that ignores empty rows, so the outline is not dragged toward blank space at the
        // top and bottom of the garment.
        private static void Smooth(float[] arr, int radius)
        {
            float[] src = (float[])arr.Clone();
            for (int i = 0; i < Rows; i++)
            {
                if (src[i] < 0f) continue;
                float sum = 0f;
                int n = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int j = i + k;
                    if (j < 0 || j >= Rows || src[j] < 0f) continue;
                    sum += src[j];
                    n++;
                }
                if (n > 0) arr[i] = sum / n;
            }
        }

        private static void RowSpan(Color32[] px, int w, int h, float v, out float l, out float r)
        {
            l = -1f;
            r = -1f;
            if (px == null) return;

            int y = Mathf.Clamp(Mathf.FloorToInt(v * h), 0, h - 1);
            int off = y * w;

            int minX = -1;
            int maxX = -1;
            for (int x = 0; x < w; x++)
            {
                if (px[off + x].a >= AlphaThreshold)
                {
                    if (minX < 0) minX = x;
                    maxX = x;
                }
            }

            if (minX < 0) return;
            l = minX / (float)w;
            r = (maxX + 1) / (float)w;
        }

        public static SARSilhouette Build(Texture2D bodyTex, Texture2D breastTex)
        {
            if (bodyTex == null) return null;

            SARSilhouette s = new SARSilhouette();

            Color32[] bodyPx = bodyTex.GetPixels32();
            int bw = bodyTex.width, bh = bodyTex.height;

            Color32[] brPx = null;
            int rw = 0, rh = 0;
            if (breastTex != null && breastTex.width > 4 && breastTex.height > 4)
            {
                // Size 0 ships as a 4x4 placeholder meaning "flat chest"; treat it as absent.
                brPx = breastTex.GetPixels32();
                rw = breastTex.width;
                rh = breastTex.height;
            }

            bool anyBody = false;

            for (int i = 0; i < Rows; i++)
            {
                float v = (i + 0.5f) / Rows;

                RowSpan(bodyPx, bw, bh, v, out float bl, out float br);
                s.refLeft[i] = bl;
                s.refRight[i] = br;
                if (bl >= 0f) anyBody = true;

                float tl = bl, tr = br;

                s.breastLeft[i] = -1f;
                s.breastRight[i] = -1f;

                if (brPx != null)
                {
                    RowSpan(brPx, rw, rh, v, out float xl, out float xr);
                    if (xl >= 0f)
                    {
                        s.breastLeft[i] = xl;
                        s.breastRight[i] = xr;

                        tl = (tl < 0f) ? xl : Mathf.Min(tl, xl);
                        tr = (tr < 0f) ? xr : Mathf.Max(tr, xr);

                        if (s.bandTopV < 0f || v < s.bandTopV) s.bandTopV = v;
                        if (v > s.bandBottomV) s.bandBottomV = v;
                        s.hasBand = true;
                    }
                }

                s.tgtLeft[i] = tl;
                s.tgtRight[i] = tr;
            }

            // Smooth the breast outline so the garment follows a round curve rather than the
            // pixel-level jitter of the alpha edge.
            BlurKeepEmpty(s.breastRight);
            BlurKeepEmpty(s.breastLeft);

            Bulge(s.breastRight, s.bulgeRight, outward: true);
            Bulge(s.breastLeft, s.bulgeLeft, outward: false);

            // Whichever side the breast actually escapes the body on is the front.
            float outR = 0f, outL = 0f;
            for (int i = 0; i < Rows; i++)
            {
                if (s.breastRight[i] >= 0f && s.refRight[i] >= 0f)
                    outR += Mathf.Max(0f, s.breastRight[i] - s.refRight[i]);
                if (s.breastLeft[i] >= 0f && s.refLeft[i] >= 0f)
                    outL += Mathf.Max(0f, s.refLeft[i] - s.breastLeft[i]);
            }
            s.frontIsRight = outR >= outL;

            s.valid = anyBody;
            return s;
        }

        // Deviation of an outline from the chord joining its own first and last covered rows.
        private static void Bulge(float[] edge, float[] outBulge, bool outward)
        {
            int first = -1, last = -1;
            for (int i = 0; i < Rows; i++)
            {
                if (edge[i] < 0f) continue;
                if (first < 0) first = i;
                last = i;
            }

            for (int i = 0; i < Rows; i++) outBulge[i] = 0f;
            if (first < 0 || last <= first) return;

            float a = edge[first], b = edge[last];
            float span = last - first;

            for (int i = first; i <= last; i++)
            {
                if (edge[i] < 0f) continue;
                float baseline = a + (b - a) * ((i - first) / span);
                float d = outward ? edge[i] - baseline : baseline - edge[i];
                outBulge[i] = Mathf.Max(0f, d);
            }
        }

        // Radius in rows, at the fixed Rows grid. Wide enough to round the join where the arm
        // overtakes the breast, narrow enough to keep the bust curve itself.
        private const int BlurRadius = 7;

        // Blur that leaves empty rows empty, so the breast outline is not smeared into the
        // rows above and below where there is no breast at all.
        private static void BlurKeepEmpty(float[] arr)
        {
            Smooth(arr, BlurRadius);
            Smooth(arr, BlurRadius);
        }

        public static int RowFor(float v)
        {
            return Mathf.Clamp(Mathf.FloorToInt(v * Rows), 0, Rows - 1);
        }

        // Linearly interpolated span lookup.
        //
        // Reading the nearest row instead quantises the warp into horizontal bands the height
        // of one grid row, which shows up as stair-stepping along a reshaped edge. Interpolating
        // makes the displacement vary smoothly down the garment regardless of grid resolution.
        // Rows with no coverage are not blended into, so the outline is not dragged toward empty
        // space at the top and bottom of the garment.
        public static float Lerp(float[] arr, float v)
        {
            float f = Mathf.Clamp(v * Rows - 0.5f, 0f, Rows - 1f);
            int i0 = Mathf.FloorToInt(f);
            int i1 = Mathf.Min(i0 + 1, Rows - 1);
            float t = f - i0;

            float a = arr[i0];
            float b = arr[i1];
            if (a < 0f) return b;
            if (b < 0f) return a;
            return a + (b - a) * t;
        }

        // How strongly the refit applies at this height: full inside the breast band,
        // easing to zero over featherV above and below it.
        //
        // Driven entirely by where SA's own breast art has coverage, so it adapts to any
        // body pack or replacement art instead of relying on hardcoded chest coordinates.
        public float BandWeight(float v, float featherV)
        {
            if (!hasBand) return 0f;
            if (v >= bandTopV && v <= bandBottomV) return 1f;
            if (featherV <= 0f) return 0f;

            float d = (v < bandTopV) ? (bandTopV - v) : (v - bandBottomV);
            // Smoothstep rather than linear: a straight ramp leaves a visible crease where the
            // reshaping starts and stops.
            return Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(d / featherV));
        }
    }
}
