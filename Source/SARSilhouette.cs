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

        // How far the bust pushes past the bare body on each side, smoothed down the body.
        // Taken raw, this profile has corners: it is max(0, bust - body), so it snaps to zero
        // wherever the arm is wider than the breast, and the result reads as an angular wedge
        // rather than a round chest. Breasts are round, so the profile driving them should be.
        public readonly float[] pushRight = new float[Rows];
        public readonly float[] pushLeft = new float[Rows];

        // Vertical extent of the breast overlay, in normalised v. -1 when absent.
        public float bandTopV = -1f;
        public float bandBottomV = -1f;

        public bool valid;
        public bool hasBand;

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
            return true;
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

                if (brPx != null)
                {
                    RowSpan(brPx, rw, rh, v, out float xl, out float xr);
                    if (xl >= 0f)
                    {
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

            for (int i = 0; i < Rows; i++)
            {
                s.pushRight[i] = (s.refRight[i] >= 0f && s.tgtRight[i] >= 0f)
                    ? Mathf.Max(0f, s.tgtRight[i] - s.refRight[i]) : 0f;
                s.pushLeft[i] = (s.refLeft[i] >= 0f && s.tgtLeft[i] >= 0f)
                    ? Mathf.Max(0f, s.refLeft[i] - s.tgtLeft[i]) : 0f;
            }

            // Two box passes approximate a gaussian closely enough and are cheap.
            BlurTwice(s.pushRight);
            BlurTwice(s.pushLeft);

            s.valid = anyBody;
            return s;
        }

        // Radius in rows, at the fixed Rows grid. Wide enough to round the join where the arm
        // overtakes the breast, narrow enough to keep the bust curve itself.
        private const int BlurRadius = 7;

        private static void BlurTwice(float[] arr)
        {
            Box(arr);
            Box(arr);
        }

        private static void Box(float[] arr)
        {
            float[] src = (float[])arr.Clone();
            for (int i = 0; i < Rows; i++)
            {
                float sum = 0f;
                int n = 0;
                for (int k = -BlurRadius; k <= BlurRadius; k++)
                {
                    int j = i + k;
                    if (j < 0 || j >= Rows) continue;
                    sum += src[j];
                    n++;
                }
                arr[i] = n > 0 ? sum / n : 0f;
            }
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
