using RimWorld;
using Verse;

namespace SizedApparelAutoRefit
{
    // Main button bar entry point.
    //
    // This replaces a Pawn.GetGizmos postfix. That patch ran for every selected pawn on every
    // frame and, being an iterator postfix, allocated an enumerator each time just to append one
    // button. A MainButtonDef is declarative and costs nothing when unused.
    //
    // Opens on the selected colonist's worn apparel when there is one, so the common case is a
    // short list rather than everything installed. The full list stays in mod settings.
    public class MainButtonWorker_SARApparel : MainButtonWorker
    {
        public override bool Visible
        {
            get
            {
                if (!base.Visible) return false;
                SARSettings s = SARMod.Settings;
                return s != null && s.enabled && SABridge.Active;
            }
        }

        public override void Activate()
        {
            Window_SARApparel open = Find.WindowStack.WindowOfType<Window_SARApparel>();
            if (open != null)
            {
                open.Close();
                return;
            }

            Find.WindowStack.Add(new Window_SARApparel(SelectedColonist()));
        }

        private static Pawn SelectedColonist()
        {
            foreach (object obj in Find.Selector.SelectedObjects)
            {
                if (obj is Pawn p && p.IsColonistPlayerControlled
                    && p.def?.race != null && p.def.race.Humanlike
                    && p.apparel?.WornApparel != null && p.apparel.WornApparel.Count > 0)
                {
                    return p;
                }
            }
            return null;
        }
    }
}
