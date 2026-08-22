# Sized Apparel Auto-Refit

Generates the sized apparel textures Sized Apparel for RJW expects, for garments it has no
hand-drawn variants for. Sized Apparel consumes them unmodified — nothing is patched on its side.

Overall this is mostly here for anyone who wants to turn this on for clothing that work with the drawn parts without needing resizing, or allow for very minor resizing in a few spots for unsupported apparel.
This is meant to appeal to anyone who wants to use the custom drawn apparel, only a few of its features, or simply just wants some granular control on a few pieces.

This resizing is still a bit rough but i feel like its "alright" enough to start testing.

## The refit

Per-scanline, horizontal only. Left and right are handled **independently** — breasts push the
outline forward only, so on side views the back edge stays put instead of smearing.

**Measure against the edge being moved.** Per row, the refit measures how far the breast overlay
sticks out past the *garment's own* edge, and pushes that edge out to cover it. The resulting
outline is the union of garment and breast, which is round because the breast outline is round.

Measuring against the *body* instead — an earlier approach — mixes baselines, because the result is
applied to the garment. The body's own taper then drives the shape: on the Hulk build the widest
part of the bulge landed about 46px below where the breast actually is widest, side views flared
below the nipple and bent back in, and on Fat in side view the garment could not reach far enough
to cover at any strength (0.135 available against 0.221 needed).

**Stretch near the edge, not across the half.** Inside the outline the displacement is weighted
toward the edge, so a belt or placket in the middle of the garment stays put instead of being
dragged outward and smeared. It is soft-limited to 1.9x the garment's own half-width to keep the
mapping from folding back on itself.

**Local falloff, not a uniform scale.** Inside the outline the cloth stretches. *Outside* it,
the outward push decays to zero over `FalloffFactor` times the push. A uniform scanline remap
displaces every pixel in proportion to its distance from the centre, so a draped garment's
sleeves fly outward far more than the bust actually moved.

**Which garments get fitted.** Everything, by default. Per garment there are two independent
controls, set from the apparel list (mod settings, or the **refit** button on the main button bar):

| control | what it does |
|---|---|
| **Front / back strength** (0-2) | reshaping applied to the south and north textures |
| **Side strength** (0-2) | reshaping applied to the east texture (west mirrors it) |
| **Off** | one-click shortcut setting both facings to 0; unticking restores the defaults |

Front and side are separate sliders because the bust protrudes much further in profile than it
does head-on. A value that looks right from the front is usually far too weak from the side, so
one number cannot serve both.

Both default to **1.0 on every build**. Strength is measured against the garment's own edge, then
corrected per build by a calibration table, so 1.0 means "correctly covered" rather than an
arbitrary amount of push. The table was measured in game as the minimum value that just covers:

| build | front / back | side |
|---|---|---|
| Female | 0.55 | 1.10 |
| Thin | 1.00 | 1.00 |
| Hulk | 0.75 | 1.05 |
| Fat | 1.00 | 1.15 |

Builds not in the table — modded ones — use 1.0 and behave as before. Saved strengths from before
the table are reset once on load, since they encode the same corrections and would otherwise
compound; garments switched off stay off.

### Entry points

- **Mod settings -> Configure apparel** — the full list of every patched garment.
- **Refit button on the main button bar** — opens the *selected colonist's* clothing only, with a
  live portrait beside it. Select a colonist first.

The per-colonist view renders the real pawn through PortraitsCache and refreshes as values
change, so a strength setting can be judged without closing the window. Texture regeneration is
held until the mouse is released, then applied once — running it mid-drag would rebake every
frame.

### Sharing settings

Export and Import write small XML profiles to
`Config/SizedApparelAutoRefit/`, containing only garments that differ from their defaults.
Importing onto a different mod list silently skips defNames that are not installed and reports
how many it applied, so a profile stays portable.

### Apparel that draws through its own render nodes

Some mods — Big and Small's armour sets among them — set `<wornGraphicPath IsNull="True"/>` and
render from `renderNodeProperties` instead. Two consequences:

- **It cannot be reshaped.** There is no worn texture to bake against, so the strength slider is
  disabled for these and the row says so.
- **It still breaks body parts unless handled.** SA's gate composes
  `WornGraphicPath + "_" + bodyType`, which for a null path is literally `"_Female"` — never
  found, so `hasUnsupportedApparel` fires and body parts vanish on the whole pawn. Registering
  that composed path fixes it.

The **draw breasts** switch does not rely on that registration. It is enforced by a postfix on
`SizedApparelUtility.CanDrawBreasts`, which is the decision Sized Apparel actually makes: if every
worn chest garment is switched on, the answer is flipped to yes; if any one is switched off, SA's
verdict stands. That gives real per-item control even for render-node apparel, where registration
alone cannot distinguish one garment from another.

### Diagnostics

Enable **Log every generated texture** in mod settings. One line per texture: body type, size,
rotation, fit strength, bust band, rows reshaped, and how far the outline moved in pixels. This
is the fastest way to tell "not generating" from "generating an identity copy".

## Notes

- Generation is lazy and cached. Startup cost is string work only.
- Each cached texture is roughly the size of the garment's own texture. The cap defaults to
  1024; past it, garments keep their vanilla shape.
- Fit strength and blend distance apply live via the settings button. The enabled toggle and
  body-type selection need a restart.
- Discarded textures are orphaned rather than destroyed — live Materials still reference
  them. Memory is reclaimed on restart.
- **Running alongside TailorMade**: it defers per-item when a record's path differs from the
  vanilla form, so it will now correctly stay out of the way of anything this mod fits.
