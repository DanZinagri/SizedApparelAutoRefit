using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SizedApparelAutoRefit
{
    // Texture plumbing: getting readable pixels out of RimWorld's GPU-only textures,
    // and cheap bilinear sampling in UV space so that sources of differing resolution
    // (256 vs 512 apparel against 512 body art) compose without special cases.
    public static class SARTex
    {
        private static readonly Dictionary<int, Texture2D> readableCache = new Dictionary<int, Texture2D>();

        // RimWorld loads textures non-readable, so GetPixels32 throws on them. Blit through
        // a temporary RenderTexture and read back. Results are cached by instance id because
        // the same body/breast art is re-read for every apparel item we generate.
        public static Texture2D Readable(Texture src)
        {
            if (src == null) return null;

            int id = src.GetInstanceID();
            if (readableCache.TryGetValue(id, out Texture2D cached) && cached != null) return cached;

            RenderTexture tmp = RenderTexture.GetTemporary(src.width, src.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture prev = RenderTexture.active;

            Graphics.Blit(src, tmp);
            RenderTexture.active = tmp;

            Texture2D readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
            readable.Apply(false, false);

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);

            readable.name = "SAR_readable_" + src.name;
            readableCache[id] = readable;
            return readable;
        }

        // ContentFinder first; on a miss, let Sized Apparel materialise a lazily loaded texture
        // and look again. Only SA's own folders are ever lazy, so the fallback costs nothing for
        // vanilla or third-party art. Confirmed against a report of an empty apparel list that
        // reproduced only with SA's lazy loading on.
        private static Texture2D Find(string path)
        {
            if (path == null) return null;

            // Order matters. Sized Apparel lazy-loads its own art, and Jikulopo Texture Cache
            // memoises ContentFinder misses permanently. Querying before SA has loaded would
            // freeze a null for that path — for us and for SA's own graphics. So for SA's folders
            // the load is requested first and ContentFinder consulted once, afterwards. That is
            // also simply the right order: there is no reason to look before asking.
            if (path.Contains("SizedApparel")) SABridge.TextureExists(path);

            return ContentFinder<Texture2D>.Get(path, false);
        }

        public static Texture2D LoadReadable(string path)
        {
            Texture2D raw = Find(path);
            return raw == null ? null : Readable(raw);
        }

        public static bool Exists(string path)
        {
            return Find(path) != null;
        }

        // Bilinear sample in UV space. Returns fully transparent outside [0,1] rather than
        // clamping to the edge — clamping smears a garment that runs to the texture border
        // out across the whole stretched region.
        public static Color32 SampleUV(Color32[] pixels, int w, int h, float u, float v)
        {
            if (u < 0f || u > 1f || v < 0f || v > 1f) return new Color32(0, 0, 0, 0);

            float fx = u * w - 0.5f;
            float fy = v * h - 0.5f;

            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0;
            float ty = fy - y0;

            int x1 = x0 + 1;
            int y1 = y0 + 1;

            x0 = Mathf.Clamp(x0, 0, w - 1);
            x1 = Mathf.Clamp(x1, 0, w - 1);
            y0 = Mathf.Clamp(y0, 0, h - 1);
            y1 = Mathf.Clamp(y1, 0, h - 1);

            Color32 c00 = pixels[y0 * w + x0];
            Color32 c10 = pixels[y0 * w + x1];
            Color32 c01 = pixels[y1 * w + x0];
            Color32 c11 = pixels[y1 * w + x1];

            // Premultiply by alpha before interpolating, then unpremultiply. Straight
            // interpolation of RGB across a transparent edge drags the (usually black)
            // colour of empty pixels into the garment outline.
            float a00 = c00.a, a10 = c10.a, a01 = c01.a, a11 = c11.a;

            float w00 = (1f - tx) * (1f - ty);
            float w10 = tx * (1f - ty);
            float w01 = (1f - tx) * ty;
            float w11 = tx * ty;

            float a = a00 * w00 + a10 * w10 + a01 * w01 + a11 * w11;
            if (a <= 0.5f) return new Color32(0, 0, 0, 0);

            float r = (c00.r * a00 * w00 + c10.r * a10 * w10 + c01.r * a01 * w01 + c11.r * a11 * w11) / a;
            float g = (c00.g * a00 * w00 + c10.g * a10 * w10 + c01.g * a01 * w01 + c11.g * a11 * w11) / a;
            float b = (c00.b * a00 * w00 + c10.b * a10 * w10 + c01.b * a01 * w01 + c11.b * a11 * w11) / a;

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(r), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(g), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(b), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(a), 0, 255));
        }

        public static void ClearReadableCache()
        {
            foreach (KeyValuePair<int, Texture2D> kv in readableCache)
            {
                if (kv.Value != null) Object.Destroy(kv.Value);
            }
            readableCache.Clear();
        }
    }
}
