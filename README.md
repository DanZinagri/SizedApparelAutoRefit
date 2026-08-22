# Sized Apparel Auto-Refit

Generates the sized apparel textures Sized Apparel for RJW expects, for garments it has no
hand-drawn variants for. Sized Apparel consumes them unmodified — nothing is patched on its side.

Overall this is mostly here for anyone who wants to turn this on for clothing that work with the drawn parts without needing resizing, or allow for very minor resizing in a few spots for unsupported apparel.
This is meant to appeal to anyone who wants to use the custom drawn apparel, only a few of its features, or simply just wants some granular control on a few pieces.

This resizing is still a bit rough but i feel like its "alright" enough to start testing.

## The refit

Per-scanline, horizontal only. Left and right are handled **independently** — breasts push the
outline forward only, so on side views the back edge stays put instead of smearing.

**Displace the garment's own outline — not the body's.** Per row, the refit measures how far
the breasts protrude past the *bare body* outline, then pushes the *garment's own* edge outward
by that amount. The displacement comes purely from the body art, so it does not matter which
body the garment was drawn against.

This matters a lot. Remapping the body outline onto the garment (the first approach) silently
assumed every garment was authored for Sized Apparel's body. Vanilla apparel is drawn for the
vanilla body, whose torso is much wider than SA's narrow one — SA's Thin torso is only ~80px
across at the bust while its breast art spans ~184px. That inflated the stretch past 2x and
splayed loose clothing into spikes.

**Local falloff, not a uniform scale.** Inside the outline the cloth stretches. *Outside* it,
the outward push decays to zero over `FalloffFactor` times the push. A uniform scanline remap
displaces every pixel in proportion to its distance from the centre, so a draped garment's
sleeves fly outward far more than the bust actually moved.

**Which garments get fitted.** Everything, by default. Per garment there are two independent
controls, set from the apparel list (mod settings, or the **refit** button on the main button bar):

| control | what it does |
|---|---|
| **Front / back strength** (0-1) | reshaping applied to the south and north textures |
| **Side strength** (0-1) | reshaping applied to the east texture (west mirrors it) |
| **Off** | one-click shortcut setting both facings to 0; unticking restores the defaults |

Front and side are separate sliders because the bust protrudes much further in profile than it
does head-on. A value that looks right from the front is usually far too weak from the side, so
one number cannot serve both. Both default to 0.85, since 1.0 overshoots and reads pointed.
| **Draw breasts** | registers a sized variant, which is what keeps body parts rendering under the garment |

They are separate because the two real cases pull opposite ways: heavy armour that breasts poke
out from behind needs reshaping, while a small harness needs none but should still let the
exposed chest draw.

Turning **Draw breasts** off hands the garment back to Sized Apparel, and SA's check is
pawn-wide — body parts then disappear on the whole pawn wearing it. That is SA's behaviour, not
something this mod can scope to one item.

### Entry points

- **Mod settings -> Configure apparel** — the full list of every patched garment.
- **Toggle-row button** (bottom right) — opens the *selected colonist's* clothing only, with a
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
