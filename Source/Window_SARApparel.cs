using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SizedApparelAutoRefit
{
    // Every garment this mod patches, with the two decisions exposed per item.
    //
    // Only apparel Sized Apparel does not already cover is listed — anything with hand-drawn
    // size variants is its own business and never appears here.
    public class Window_SARApparel : Window
    {
        private Vector2 scroll;
        private string search = "";
        private readonly List<ThingDef> visible = new List<ThingDef>();
        private readonly List<ThingDef> worn = new List<ThingDef>();

        // Rebuilding the registry walks every def and probes the filesystem, so changes are
        // batched and applied once on close rather than on every click.
        private bool dirty;
        private bool registryDirty;
        private bool pendingApply;

        private const float RowHeight = 30f;
        private const float ToggleWidth = 110f;
        private const float SliderWidth = 150f;
        private const float OffWidth = 46f;

        // Right-hand controls: front slider, side slider, off, draw breasts.
        private const float ControlsWidth = SliderWidth * 2f + OffWidth + ToggleWidth;

        public override Vector2 InitialSize => pawn != null ? new Vector2(980f, 720f) : new Vector2(980f, 720f);

        private const float PreviewWidth = 240f;
        private Rot4 previewRot = Rot4.South;

        private readonly Pawn pawn;

        public Window_SARApparel(Pawn pawn = null)
        {
            this.pawn = pawn;
            doCloseX = true;
            doCloseButton = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            SARSettings s = SARMod.Settings;
            List<ThingDef> all = SARRegistry.Patched;

            // When opened from a colonist, narrow to what they are actually wearing.
            if (pawn?.apparel?.WornApparel != null)
            {
                worn.Clear();
                for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
                {
                    ThingDef d = pawn.apparel.WornApparel[i].def;
                    if (all.Contains(d) && !worn.Contains(d)) worn.Add(d);
                }
                all = worn;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f),
                pawn != null ? "Refit apparel — " + pawn.LabelShortCap : "Patched apparel");
            Text.Font = GameFont.Small;

            Rect sub = new Rect(0f, 32f, inRect.width, 40f);
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(sub, $"{all.Count} garments. Sized Apparel already covers the rest, and those are left alone.\n"
                + "Refit reshapes the garment to the bust. Draw breasts keeps body parts rendering underneath it.");
            GUI.color = Color.white;

            Rect searchRect = new Rect(0f, 76f, 320f, 28f);
            string newSearch = Widgets.TextField(searchRect, search);
            if (newSearch != search) search = newSearch;

            if (Widgets.ButtonText(new Rect(332f, 76f, 120f, 28f), "Refit: all on")) BulkRefit(true);
            if (Widgets.ButtonText(new Rect(460f, 76f, 120f, 28f), "Refit: all off")) BulkRefit(false);
            DrawProfileButtons(new Rect(inRect.width - 348f, 40f, 348f, 28f));

            if (Widgets.ButtonText(new Rect(588f, 76f, 120f, 28f), "Reset to defaults"))
            {
                s.strengths.Clear();
                s.strengthsSide.Clear();
                s.breastsOn.Clear(); s.breastsOff.Clear();
                dirty = true;
                registryDirty = true;
                pendingApply = true;
            }

            visible.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef d = all[i];
                if (search.NullOrEmpty()
                    || d.label.ToLower().Contains(search.ToLower())
                    || d.defName.ToLower().Contains(search.ToLower()))
                {
                    visible.Add(d);
                }
            }

            Rect head = new Rect(0f, 112f, inRect.width - 16f - (pawn != null ? PreviewWidth : 0f), 24f);
            Widgets.Label(new Rect(head.x + 4f, head.y, 300f, 24f), "Garment");
            Widgets.Label(new Rect(head.xMax - ControlsWidth, head.y, SliderWidth, 24f), "Front / back");
            Widgets.Label(new Rect(head.xMax - SliderWidth - OffWidth - ToggleWidth, head.y, SliderWidth, 24f), "Side");
            Widgets.Label(new Rect(head.xMax - OffWidth - ToggleWidth, head.y, OffWidth, 24f), "Off");
            Widgets.Label(new Rect(head.xMax - ToggleWidth, head.y, ToggleWidth, 24f), "Draw breasts");
            Widgets.DrawLineHorizontal(0f, head.yMax, head.width);

            float listWidth = inRect.width - (pawn != null ? PreviewWidth : 0f);
            if (pawn != null) DrawPreview(new Rect(listWidth + 8f, head.yMax + 4f, PreviewWidth - 8f, 360f));

            Rect outer = new Rect(0f, head.yMax + 4f, listWidth, inRect.height - head.yMax - 60f);
            Rect view = new Rect(0f, 0f, outer.width - 20f, visible.Count * RowHeight + 4f);

            Widgets.BeginScrollView(outer, ref scroll, view);
            for (int i = 0; i < visible.Count; i++)
            {
                Rect row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                if (row.yMax < scroll.y || row.y > scroll.y + outer.height) continue;   // cull offscreen
                DrawRow(row, visible[i], s, i);
            }
            Widgets.EndScrollView();
        }

        private void DrawStrength(Rect rect, ThingDef def, SARSettings s, SARPolicy.Facing facing, float value)
        {
            float updated = Widgets.HorizontalSlider(rect, value, 0f, 1f,
                middleAlignment: false, value <= 0.001f ? "off" : value.ToString("0.00"));

            if (Mathf.Abs(updated - value) > 0.001f)
            {
                SARPolicy.SetStrength(def, s, facing, updated);
                dirty = true;
                pendingApply = true;
            }
        }

        private void DrawRow(Rect row, ThingDef def, SARSettings s, int index)
        {
            if (index % 2 == 1) Widgets.DrawLightHighlight(row);
            Widgets.DrawHighlightIfMouseover(row);

            float front = SARPolicy.Strength(def, s, SARPolicy.Facing.Front);
            float side = SARPolicy.Strength(def, s, SARPolicy.Facing.Side);
            bool breasts = SARPolicy.KeepBreasts(def, s);
            bool canRefit = SARRegistry.CanRefit(def);

            string layer = def.apparel?.LastLayer?.label ?? "";
            Widgets.Label(new Rect(row.x + 4f, row.y + 4f, row.width - ControlsWidth - 12f, row.height),
                def.LabelCap + "  <color=#888888>(" + layer + ")</color>");

            Rect rFront = new Rect(row.xMax - ControlsWidth, row.y + 6f, SliderWidth - 10f, 20f);
            Rect rSide = new Rect(row.xMax - SliderWidth - OffWidth - ToggleWidth, row.y + 6f, SliderWidth - 10f, 20f);

            if (canRefit)
            {
                DrawStrength(rFront, def, s, SARPolicy.Facing.Front, front);
                DrawStrength(rSide, def, s, SARPolicy.Facing.Side, side);
            }
            else
            {
                GUI.color = new Color(0.55f, 0.55f, 0.55f);
                Rect rNote = new Rect(rFront.x, rFront.y, SliderWidth * 2f - 10f, 20f);
                Widgets.Label(rNote, "no worn texture");
                TooltipHandler.TipRegion(rNote, "This garment draws through its own render nodes rather than a "
                    + "worn graphic path, so its texture cannot be reshaped from here. Draw breasts still works.");
                GUI.color = Color.white;
            }

            // One-click off, so turning a garment (or a filtered batch) off does not mean dragging
            // sliders to zero each time. Acts on both facings, and unticking restores the defaults
            // rather than whatever the sliders happened to be at.
            Rect rOff = new Rect(row.xMax - OffWidth - ToggleWidth, row.y + 4f, 24f, 24f);
            if (canRefit)
            {
                bool isOff = front <= 0.001f && side <= 0.001f;
                bool newOff = isOff;
                Widgets.Checkbox(rOff.position, ref newOff);
                if (newOff != isOff)
                {
                    SARPolicy.SetStrengthAll(def, s, newOff ? 0f : SARPolicy.DefaultStrength);
                    dirty = true;
                    pendingApply = true;
                }
            }

            Rect rBreasts = new Rect(row.xMax - ToggleWidth, row.y + 4f, 24f, 24f);
            bool newBreasts = breasts;
            Widgets.Checkbox(rBreasts.position, ref newBreasts);
            if (newBreasts != breasts)
            {
                SARPolicy.SetKeepBreasts(def, s, newBreasts);
                // Registration decides this, so the advertised path set must be rebuilt.
                dirty = true;
                registryDirty = true;
                pendingApply = true;
            }
        }

        // Live portrait of the actual colonist, so a change can be judged without closing the
        // window. PortraitsCache does the rendering; we only invalidate it once the regenerated
        // textures are in place.
        private void DrawPreview(Rect rect)
        {
            Widgets.DrawMenuSection(rect);

            Rect rotRow = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, 28f);
            if (Widgets.ButtonText(new Rect(rotRow.x, rotRow.y, 62f, 28f), "Front")) previewRot = Rot4.South;
            if (Widgets.ButtonText(new Rect(rotRow.x + 66f, rotRow.y, 62f, 28f), "Side")) previewRot = Rot4.East;
            if (Widgets.ButtonText(new Rect(rotRow.x + 132f, rotRow.y, 62f, 28f), "Back")) previewRot = Rot4.North;

            Rect img = new Rect(rect.x + 6f, rotRow.yMax + 6f, rect.width - 12f, rect.height - 76f);
            try
            {
                RenderTexture tex = PortraitsCache.Get(pawn, new Vector2(img.width, img.height), previewRot,
                    default(Vector3), 1.1f, true, true, true, true);
                GUI.DrawTexture(img, tex);
            }
            catch
            {
                Widgets.Label(img, "Preview unavailable.");
            }

            if (pendingApply)
            {
                GUI.color = new Color(0.75f, 0.7f, 0.4f);
                Widgets.Label(new Rect(rect.x + 6f, rect.yMax - 26f, rect.width - 12f, 24f), "Rebuilding textures...");
                GUI.color = Color.white;
            }
        }

        // Regenerating textures is far too heavy to run while a slider is being dragged, so
        // changes are held until the mouse is released and then applied once.
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (!pendingApply || Input.GetMouseButton(0)) return;

            pendingApply = false;
            SARMod.ApplyLive(registryDirty);
            registryDirty = false;

            if (pawn != null)
            {
                PortraitsCache.SetDirty(pawn);
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }

        private void DrawProfileButtons(Rect rect)
        {
            if (Widgets.ButtonText(new Rect(rect.x, rect.y, 104f, 28f), "Export..."))
            {
                Find.WindowStack.Add(new Dialog_SARProfileName(delegate (string name)
                {
                    if (SARProfile.Export(name, SARMod.Settings, out string err))
                        Messages.Message("Exported profile: " + name, MessageTypeDefOf.TaskCompletion, false);
                    else
                        Messages.Message("Export failed: " + err, MessageTypeDefOf.RejectInput, false);
                }));
            }

            if (Widgets.ButtonText(new Rect(rect.x + 110f, rect.y, 104f, 28f), "Import..."))
            {
                List<string> profiles = SARProfile.AvailableProfiles();
                if (profiles.Count == 0)
                {
                    Messages.Message("No saved profiles yet. Export one first.", MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    List<FloatMenuOption> opts = new List<FloatMenuOption>();
                    foreach (string name in profiles)
                    {
                        string captured = name;
                        opts.Add(new FloatMenuOption(captured, delegate
                        {
                            SARProfile.Import(captured, SARMod.Settings, out string summary);
                            Messages.Message(summary, MessageTypeDefOf.TaskCompletion, false);
                            dirty = true;
                            registryDirty = true;
                            pendingApply = true;
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
            }

            if (Widgets.ButtonText(new Rect(rect.x + 220f, rect.y, 124f, 28f), "Open folder"))
            {
                Application.OpenURL(SARProfile.FolderPath);
            }
        }

        private void BulkRefit(bool value)
        {
            SARSettings s = SARMod.Settings;
            for (int i = 0; i < visible.Count; i++)
                SARPolicy.SetStrengthAll(visible[i], s, value ? SARPolicy.DefaultStrength : 0f);
            dirty = true;
            pendingApply = true;
        }

        public override void PreClose()
        {
            base.PreClose();
            SARMod.Settings?.Write();

            if (dirty)
            {
                SARMod.ApplyLive(registryDirty);
                dirty = false;
                registryDirty = false;
            }
        }
    }
}
