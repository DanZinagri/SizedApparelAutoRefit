using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SizedApparelAutoRefit
{
    public class SARSettings : ModSettings
    {
        public bool enabled = true;

        // Multiplier on how far the garment follows the bust outline. 1 tracks the drawn
        // silhouette exactly; lower is subtler, higher exaggerates.
        public float bulgeGain = 1f;

        // Blend distance above and below the bust band, in fractions of body height.
        public float featherPercent = 6f;

        public int maxCachedTextures = 1024;

        // Explicit per-garment choices, stored as deviations from the tag/extension defaults so
        // the config stays small and newly installed apparel picks up sane defaults on its own.
        public Dictionary<string, float> strengths = new Dictionary<string, float>();      // front and back
        public Dictionary<string, float> strengthsSide = new Dictionary<string, float>();  // east and west

        public HashSet<string> breastsOn = new HashSet<string>();
        public HashSet<string> breastsOff = new HashSet<string>();

        // Bumped when the maths changes in a way that invalidates saved magnitudes.
        public int calibrationVersion;

        public bool logGeneration;

        public List<string> disabledBodyTypes = new List<string>();

        public float FeatherV => Mathf.Max(0f, featherPercent / 100f);

        public bool BodyTypeEnabled(string defName)
        {
            return disabledBodyTypes == null || !disabledBodyTypes.Contains(defName);
        }

        public void SetBodyTypeEnabled(string defName, bool value)
        {
            if (disabledBodyTypes == null) disabledBodyTypes = new List<string>();
            if (value) disabledBodyTypes.Remove(defName);
            else if (!disabledBodyTypes.Contains(defName)) disabledBodyTypes.Add(defName);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref bulgeGain, "bulgeGain", 1f);
            Scribe_Values.Look(ref featherPercent, "featherPercent", 6f);
            Scribe_Values.Look(ref maxCachedTextures, "maxCachedTextures", 1024);
            Scribe_Collections.Look(ref strengths, "strengths", LookMode.Value, LookMode.Value);
            if (strengths == null) strengths = new Dictionary<string, float>();
            Scribe_Collections.Look(ref strengthsSide, "strengthsSide", LookMode.Value, LookMode.Value);

            // Settings written before front and side were separated carry one value meaning both.
            // Copy it across rather than silently resetting side to the default.
            if (strengthsSide == null)
            {
                strengthsSide = new Dictionary<string, float>(strengths);
            }
            ScribeSet(ref breastsOn, "breastsOn");
            ScribeSet(ref breastsOff, "breastsOff");
            Scribe_Values.Look(ref calibrationVersion, "calibrationVersion", 0);
            Scribe_Values.Look(ref logGeneration, "logGeneration", false);
            Scribe_Collections.Look(ref disabledBodyTypes, "disabledBodyTypes", LookMode.Value);
            if (disabledBodyTypes == null) disabledBodyTypes = new List<string>();

            if (Scribe.mode == LoadSaveMode.LoadingVars) MigrateCalibration();
        }

        // Strength used to be a raw multiplier whose "correct" value differed per build, so saved
        // values encode corrections that the calibration table now applies itself. Left alone they
        // would compound. Values of zero are kept, because those mean "this garment is switched
        // off" rather than a magnitude.
        private const int CurrentCalibrationVersion = 1;

        private void MigrateCalibration()
        {
            if (calibrationVersion >= CurrentCalibrationVersion) return;

            int reset = Strip(strengths) + Strip(strengthsSide);
            calibrationVersion = CurrentCalibrationVersion;

            if (reset > 0)
            {
                Log.Message($"[SA Auto-Refit] Reset {reset} saved refit strengths to the new calibrated "
                            + "default of 1.0. Garments switched off stayed off.");
            }
        }

        private static int Strip(Dictionary<string, float> store)
        {
            if (store == null) return 0;
            List<string> drop = new List<string>();
            foreach (KeyValuePair<string, float> kv in store)
            {
                if (kv.Value > 0.001f) drop.Add(kv.Key);
            }
            for (int i = 0; i < drop.Count; i++) store.Remove(drop[i]);
            return drop.Count;
        }

        private static void ScribeSet(ref HashSet<string> set, string label)
        {
            List<string> list = set == null ? new List<string>() : new List<string>(set);
            Scribe_Collections.Look(ref list, label, LookMode.Value);
            set = list == null ? new HashSet<string>() : new HashSet<string>(list);
        }
    }

    public class SARMod : Mod
    {
        public static SARSettings Settings;


        public SARMod(ModContentPack content) : base(content)
        {
            // Settings only. Harmony patching is deliberately NOT done here — see SARStartup.
            Settings = GetSettings<SARSettings>();
        }

        public override string SettingsCategory() => "Sized Apparel Auto-Refit";

        private enum Tab { General, BodyTypes }

        private Tab tab = Tab.General;
        private Vector2 scrollGeneral;
        private Vector2 scrollBodies;

        // Content height measured on the previous frame, so the scroll view fits what it
        // actually holds instead of a fixed guess that overflows.
        private float generalHeight = 600f;

        // Which builds actually have Sized Apparel breast art. Probing textures is not free, so
        // it is computed once when the tab is first shown.
        private HashSet<string> eligibleBodyTypes;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // TabDrawer draws the tab strip above the rect it is given, so leave room for it.
            Rect body = new Rect(inRect.x, inRect.y + 32f, inRect.width, inRect.height - 32f);

            List<TabRecord> tabs = new List<TabRecord>
            {
                new TabRecord("General", delegate { tab = Tab.General; }, tab == Tab.General),
                new TabRecord("Body types", delegate { tab = Tab.BodyTypes; }, tab == Tab.BodyTypes),
            };
            TabDrawer.DrawTabs(body, tabs);

            if (tab == Tab.General) DrawGeneral(body);
            else DrawBodyTypes(body);
        }

        private void DrawGeneral(Rect rect)
        {
            Rect view = new Rect(0f, 0f, rect.width - 20f, generalHeight);
            Widgets.BeginScrollView(rect, ref scrollGeneral, view);

            Listing_Standard list = new Listing_Standard();
            list.Begin(view);

            if (!SABridge.Active)
            {
                GUI.color = Color.yellow;
                list.Label("Sized Apparel for RJW is not loaded. This mod does nothing without it.");
                GUI.color = Color.white;
                list.GapLine(12f);
            }

            list.CheckboxLabeled("Enabled", ref Settings.enabled,
                "Generate sized apparel textures for garments Sized Apparel has no hand-drawn variants for. "
                + "Hand-drawn art is never overridden.");

            list.Gap(6f);
            list.Label($"Fit strength: {Settings.bulgeGain:0.00}x",
                -1f, new TipSignal("How closely the garment follows the bust outline. 1.00 tracks the drawn silhouette exactly."));
            Settings.bulgeGain = list.Slider(Settings.bulgeGain, 0f, 2f);

            list.Label($"Blend distance: {Settings.featherPercent:0}%",
                -1f, new TipSignal("How far above and below the bust the reshaping eases back to the garment's original shape. "
                     + "Too small creates a visible seam; too large drags the shoulders and waist around."));
            Settings.featherPercent = Mathf.Round(list.Slider(Settings.featherPercent, 0f, 25f));

            list.Gap(6f);
            list.Label($"Generated texture cap: {Settings.maxCachedTextures}",
                -1f, new TipSignal("Upper bound on cached generated textures. Each is roughly the size of the garment's own "
                     + "texture. Past the cap, further garments keep their vanilla shape."));
            Settings.maxCachedTextures = Mathf.RoundToInt(list.Slider(Settings.maxCachedTextures, 128f, 4096f) / 128f) * 128;

            list.GapLine(12f);
            if (list.ButtonText("Configure apparel..."))
            {
                Find.WindowStack.Add(new Window_SARApparel());
            }
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            list.Label("Every garment is refitted by default. Open the list to turn refitting off for "
                       + "individual items, or to stop body parts drawing under them. Modders can set the "
                       + "same two flags with a SizedApparelAutoRefit.SARFitExtension, or the tags "
                       + "SAR_NoRefit / SAR_ForceRefit.");
            GUI.color = Color.white;

            list.GapLine(12f);
            list.Label($"Registered variants: {SARRegistry.RegisteredCount} across {SARRegistry.ApparelCount} apparel defs");
            list.Label($"Generated this session: {SARGenerator.GeneratedCount}");

            list.CheckboxLabeled("Log every generated texture", ref Settings.logGeneration,
                "Writes one line per generated texture: body type, size, rotation, fit strength, bust band, "
                + "rows reshaped and how far the outline moved. Use this to diagnose garments that look wrong.");

            if (list.ButtonText("Apply fit changes now"))
            {
                ApplyLive();
            }

            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            list.Label("Fit strength and blend distance apply immediately with the button above. "
                       + "Changes to the enabled toggle or body types need a restart, and memory from discarded "
                       + "textures is only reclaimed on restart.");
            GUI.color = Color.white;

            generalHeight = list.CurHeight + 16f;
            list.End();
            Widgets.EndScrollView();
        }

        private void DrawBodyTypes(Rect rect)
        {
            if (eligibleBodyTypes == null)
            {
                eligibleBodyTypes = new HashSet<string>();
                foreach (BodyTypeDef bt in SARBodyTypes.Eligible()) eligibleBodyTypes.Add(bt.defName);
            }

            List<BodyTypeDef> all = new List<BodyTypeDef>();
            foreach (BodyTypeDef bt in DefDatabase<BodyTypeDef>.AllDefsListForReading)
            {
                if (bt?.defName == null) continue;
                if (bt == BodyTypeDefOf.Baby || bt == BodyTypeDefOf.Child) continue;
                all.Add(bt);
            }
            all.Sort((x, y) => string.Compare(x.defName, y.defName, System.StringComparison.OrdinalIgnoreCase));

            float rowH = Text.LineHeight + 6f;
            float headerH = 70f;
            Rect view = new Rect(0f, 0f, rect.width - 20f, headerH + all.Count * rowH + 16f);
            Widgets.BeginScrollView(rect, ref scrollBodies, view);

            Listing_Standard list = new Listing_Standard();
            list.Begin(view);

            list.Label($"{all.Count} body types. Untick a build to leave its apparel alone. Changes need a restart.");
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            list.Label("Builds without Sized Apparel breast art have no bust outline to refit to and are greyed out; "
                       + "ticking them does nothing until art exists for them.");
            GUI.color = Color.white;
            list.GapLine(6f);

            for (int i = 0; i < all.Count; i++)
            {
                BodyTypeDef bt = all[i];
                bool eligible = eligibleBodyTypes.Contains(bt.defName);

                bool on = Settings.BodyTypeEnabled(bt.defName);
                bool was = on;

                if (!eligible) GUI.color = new Color(0.55f, 0.55f, 0.55f);
                list.CheckboxLabeled(bt.defName + (eligible ? "" : "   (no breast art)"), ref on,
                    eligible ? null : "Sized Apparel ships no breast art for this build, so there is no bust outline to refit to.");
                GUI.color = Color.white;

                if (on != was) Settings.SetBodyTypeEnabled(bt.defName, on);
            }

            list.End();
            Widgets.EndScrollView();
        }

        // Rebuilds everything downstream of the fit parameters.
        //
        // Old textures are orphaned rather than destroyed: live Materials still point at
        // them, and freeing one out from under a Material renders the garment invisible.
        public static void ApplyLive(bool rebuildRegistry = false)
        {
            SARGenerator.ClearCache();
            SABridge.ClearResolvedPathCaches();
            GraphicDatabase.Clear();
            PortraitsCache.Clear();

            if (Find.Maps != null)
            {
                foreach (Map map in Find.Maps)
                {
                    foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                    {
                        p.Drawer?.renderer?.SetAllGraphicsDirty();
                    }
                }
            }

            Messages.Message("Sized Apparel Auto-Refit: regenerating fitted apparel.",
                MessageTypeDefOf.TaskCompletion, false);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
        }
    }
}
