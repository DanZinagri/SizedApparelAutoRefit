using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace SizedApparelAutoRefit
{
    // Registration runs once at startup, after defs and after Sized Apparel's own static
    // setup. Only paths are produced here; no image work happens until something is worn.
    [StaticConstructorOnStartup]
    public static class SARStartup
    {
        static SARStartup()
        {
            // Patch here rather than in the Mod constructor.
            //
            // Mod constructors run during CreateModClasses, before defs or textures are loaded.
            // Patching a Sized Apparel method there forces Harmony to compile it, which runs
            // SizedApparelUtility's static constructor far too early: its ChestBPG field does a
            // DefDatabase lookup that fails, and — much worse — textureExistsSet snapshots every
            // mod's texture list, which at that point is empty and stays cached for the session.
            // SA would then treat every garment as unsupported. StaticConstructorOnStartup runs
            // after defs and content are loaded, so the same patching is safe.
            new Harmony("DanZinagri.SizedApparelAutoRefit").PatchAll();

            if (!SABridge.Active)
            {
                Log.Message("[SA Auto-Refit] Sized Apparel for RJW not detected — standing down.");
                return;
            }

            if (!SABridge.UseBodyTexture)
            {
                Log.Message("[SA Auto-Refit] Sized Apparel's body textures are disabled, so it draws the vanilla "
                            + "silhouette and there is nothing to refit against. Standing down.");
                return;
            }

            SARRegistry.Build();
        }
    }

    // The generation hook.
    //
    // Graphic_Multi.Init is where the per-rotation and per-mask ContentFinder lookups happen,
    // so injecting into the content holder from a prefix here means those lookups simply
    // succeed. Non-generic instance method on a non-generic class, and far too large to
    // inline.
    //
    // This is the seam that actually carries Sized Apparel's requests. It asks for
    // GraphicDatabase.Get<Graphic_Multi>(...), and every generic Get<T> overload calls
    // GetInner<T> directly — the private non-generic Get(GraphicRequest) is only reached from
    // the Get(Type graphicClass, ...) overloads. Patching that one alone never fires here.
    [HarmonyPatch(typeof(Graphic_Multi), nameof(Graphic_Multi.Init))]
    internal static class Patch_Graphic_Multi_Init
    {
        private static void Prefix(GraphicRequest req)
        {
            SARGenerator.EnsureFor(req.path);
        }
    }

    // Secondary coverage for callers that build graphics through the non-generic
    // GraphicDatabase.Get(Type graphicClass, ...) entry point, which routes via
    // Get(GraphicRequest) rather than GetInner directly. EnsureFor is idempotent, so
    // overlapping with the Graphic_Multi hook costs one hash lookup.
    //
    // Do NOT patch ContentFinder<Texture2D>.Get instead. ContentFinder<T> is generic and
    // the CLR shares a single native code body across all reference-type instantiations, so
    // patching the Texture2D instantiation also rewrites ContentFinder<AudioClip> and
    // ContentFinder<Shader> with T mis-bound to Texture2D. The symptom is a flood of
    // "Could not load Texture2D at '<some audio path>'" errors out of AudioGrain_Clip and
    // sounds that never load. This seam is non-generic and carries no such hazard.
    [HarmonyPatch]
    internal static class Patch_GraphicDatabase_Get
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GraphicDatabase), "Get",
                new[] { typeof(GraphicRequest) });
        }

        // Runs for every graphic request in the game, so EnsureFor rejects anything that is
        // not ours in two hash lookups.
        private static void Prefix(GraphicRequest req)
        {
            SARGenerator.EnsureFor(req.path);
        }
    }
}
