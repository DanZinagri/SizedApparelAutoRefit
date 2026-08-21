# Sized Apparel Auto-Refit

Generates the sized apparel textures Sized Apparel for RJW expects, for garments it has no
hand-drawn variants for. Sized Apparel consumes them unmodified — nothing is patched on its side.

## Why this exists

Sized Apparel only resizes clothing it ships art for. Two consequences:

1. Unsupported garments keep their vanilla shape.
2. `hasUnSupportedApparelFromWornData` is a **pawn-wide** gate. If any torso/chest garment
   lacks a sized texture, `hasUnsupportedApparel` flips true and sizing is skipped for
   *every* garment on that colonist, and their breasts are hidden. One unsupported shirt
   disables the mod for the whole pawn.

Generic refitters don't fix this because they detect body mods by **path shadowing** — they
read `bodyType.bodyNakedGraphicPath`. Sized Apparel never occupies that path. It takes the
vanilla path, appends `_BaseBody` (+ `F`/`M`, + variation), and installs the result from a
postfix on `PawnRenderNode_Body.GraphicFor`. A refitter reading the def path gets the
*vanilla* body as both source and target, so its warp is an identity transform.

## How it hooks in

Three seams, all verified against Sized Apparel 5.3.6 and RimWorld 1.6:

| Seam | Purpose |
|---|---|
| `SizedApparelUtility.textureExistsSet` | Advertise that a sized variant exists |
| `Graphic_Multi.Init(GraphicRequest)` (prefix) | Generate and inject before the graphic's own lookups run |
| `GraphicDatabase.Get(GraphicRequest)` (prefix) | Secondary coverage for the `Get(Type, …)` entry point |
| This mod's `GetContentHolder<Texture2D>().contentList` | Where the pixels are handed over |
| `SizedApparelsDatabase.ClearAll()` | Flush negative caches after registration |

### Never patch `ContentFinder<T>`

The obvious hook — a postfix on `ContentFinder<Texture2D>.Get` that generates on a miss —
**is unsafe and was tried first.** `ContentFinder<T>` is a generic type, and the CLR shares a
single JIT-compiled native body across all reference-type instantiations. Patching the
`Texture2D` instantiation therefore also rewrites `ContentFinder<AudioClip>` and
`ContentFinder<Shader>`, with `T` mis-bound to `Texture2D`. Symptom:

```
Could not load Texture2D at 'Pawn/Abilities/AcidSpray/Acid_Spray_Cast'
  ... ContentFinder`1[UnityEngine.Texture2D].Get_Patch1 (string,bool)
  Verse.Sound.AudioGrain_Clip/<GetResolvedGrains>d__1:MoveNext ()
```

A texture-typed error for an audio path, raised from the audio loader — plus sounds that never
load. `Graphic_Multi.Init` is used instead: non-generic, on a non-generic class, and far too
large to inline.

Note that `GraphicDatabase.Get(GraphicRequest)` alone is **not** sufficient, which cost another
iteration. All nine generic `Get<T>` overloads call `GetInner<T>` directly; only the
`Get(Type graphicClass, …)` overloads route through the non-generic one, and Sized Apparel does
not use those. `GetInner<T>` is itself generic, so it is off-limits for the same reason as
`ContentFinder<T>`.

Calling generic methods is fine — only patching shared generic code is the hazard. Injection
uses `GetContentHolder<Texture2D>()` (public) because `ModContentPack.textures` is private in
current builds. Only `contentList` is needed; `ModContentHolder.Get` does not consult the trie.

All coupling is reflection-only (`SABridge`). Sized Apparel's assembly is never referenced —
it hard-depends on RJW, and a drifted API degrades to "do nothing" instead of erroring.

### Patch at StaticConstructorOnStartup, never in the Mod constructor

Mod constructors run during `CreateModClasses`, **before defs or textures are loaded**. Patching
any Sized Apparel method there makes Harmony compile it, which runs `SizedApparelUtility`'s static
constructor at that moment. Two things break:

```
Failed to find Verse.BodyPartGroupDef named ChestBPG. There are 0 defs of this type loaded.
  Verse.DefDatabase`1<BodyPartGroupDef>:GetNamed (string,bool)
  SizedApparel.SizedApparelUtility:.cctor ()
  ... HarmonyLib.Harmony:PatchAll () / SARMod:.ctor (ModContentPack)
```

The visible error is the `ChestBPG` lookup failing. The damaging one is silent: the same class
initialises `textureExistsSet` by snapshotting every running mod's texture list, which at that
point is empty and is then cached for the whole session — Sized Apparel would treat every garment
as unsupported.

All patching therefore happens in `SARStartup`, which is `[StaticConstructorOnStartup]` and runs
after defs and content are loaded. The Mod constructor only reads settings.

### Lazy loading is not a factor

`TextureExists` always ends with `return textureExistsSet.Contains(path)`. Its lazy branch
only triggers for paths in `lazyTextureSet` — real files SA stripped out of the content list.
Synthetic paths are never files, so both lazy-on and lazy-off reach the same check.

One caveat: SA's `GetAllFilesForMod` postfix lazy-strips any texture path containing
`SizedApparel` **from every mod**, not just its own. Don't put real shipped textures under a
folder with that in the name.

### Path convention emitted

```
{wornGraphicPath}_{bodyType}[M]_{sizeIndex}_{south|north|east}
```

Mirrors `CheckSizedApparelGraphicExists`, minus the race segment — SA falls back to the
race-less form for every race, so one registration covers humans and alien races alike.
Masks resolve as `…_{rot}m`, matching `Graphic_Multi`. West is intentionally not generated;
Graphic_Multi mirrors east, exactly as it does for SA's own art.

Hand-drawn art always wins: a variant is only registered where SA has none.

### Registration is never conditional on fit strength

Every chest garment gets registered for sizes 0-10, **including outerwear set to zero fit**.

This is not optional. `hasUnSupportedApparelFromWornData` probes
`wornGraphicPath + "_" + bodyType` for each worn chest garment, and a single miss sets
`hasUnsupportedApparel`, which stops Sized Apparel sizing *every* garment on that colonist and
hides their breasts. Skipping registration for jackets and parkas — briefly tried, because they
reshape nothing — therefore switched the whole pawn off and suppressed body parts entirely. It
recreated the exact bug this mod exists to fix.

Fit strength controls only how much the texture is reshaped. A zero-fit garment still gets a
registered variant; the generator just hands back a **borrowed reference** to the original
texture instead of allocating a reshaped copy, so it costs no memory and never counts against
the texture cap. Borrowed textures are never renamed or freed — they belong to whichever mod
supplied the art.

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
| **Refit strength** (0-1) | how far the texture is reshaped toward the bust; defaults to 0.85, since 1.0 overshoots and reads pointed |
| **Off** | one-click shortcut for strength 0; unticking restores the default |
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

### Entry points, and why there is no gizmo

The full list lives in mod settings. In-game there is a minimized **refit** button on the main
button bar (`MainButtonDef`, order 95, alongside History/Factions/Ideos), which opens the list
filtered to the selected colonist's worn apparel — usually a handful of items rather than
everything installed.

It is deliberately *not* a pawn gizmo. A `Pawn.GetGizmos` postfix runs for every selected pawn
every frame and, being an iterator postfix, allocates an enumerator each time merely to append one
button. A `MainButtonDef` is declarative and costs nothing when unused. The same reasoning removed
the `PlaySettings` toggle-row button, leaving no UI patches at all — only the three functional
ones.

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

### Why there is no automatic classifier

Three were built and all three were wrong in ways that only showed up on a pawn:

- **Apparel layer.** Wrong in both directions — measured against SA's own art, a Robe (`Shell`)
  wants full fitting while TribalA (`OnSkin`) wants none.
- **Armour rating.** Actively inverted: vanilla's highest `ArmorRating_Sharp` is the flak vest
  (1.00), the garment that benefits *most*, while plate armour reads 0.00 because plate takes its
  protection from its stuff rather than `statBases`.
- **Measured drape** (garment width over body width across the bust band). Separates all eight
  test garments cleanly on a slim build — 1.065-1.116 fitted, 0.543 and 1.395-1.525 not — but the
  thresholds move per body type (on Fat, Robe at 1.008 and Jacket at 1.026 are 0.018 apart), and
  it was calibrated only on garments SA retextured. It could not be trusted for the vanilla and
  modded art it would actually face.

Each rule was right for most garments and confidently wrong for some, with no in-game signal that
it had guessed. A list you can see and change is worth more than a heuristic that is right 85% of
the time and silent about the rest.

Verified compositing assumptions: the `Breasts` bone offset is `(0,0,0)` for every body type
and `scale` is 1, so the overlay composites 1:1 onto the body quad at matching dimensions.

## Validation

Warping SA's own base garment up to size N, scored (alpha IoU) against SA's hand-drawn variant,
over the eight garments that ship both sized art and a base texture — the complete set. Each
figure is the mean of 12 cases (Thin and Fat builds, south and east, sizes 3/5/6).

| garment | layer | fit 0.00 | fit 1.00 | ideal |
|---|---|---|---|---|
| ShirtBasic | OnSkin | 0.8422 | **0.9753** | 1.0 |
| ShirtButton | OnSkin | 0.8455 | **0.9744** | 1.0 |
| FlakVest | Middle | 0.7803 | **0.9284** | 1.0 |
| Robe | Shell | 0.8463 | **0.9469** | 1.0 |
| TribalA | OnSkin | **0.8861** | 0.8216 | 0.0 |
| Jacket | Shell | **0.9119** | 0.8367 | 0.0 |
| Parka | Shell | **0.9342** | 0.8447 | 0.0 |
| FlakJacket | Shell | **0.9548** | 0.8611 | 0.0 |

The drape measurement reproduces all eight verdicts. On the reference build the four that want
fitting land in 1.065-1.116; TribalA sits at 0.543 and the boxy outerwear at 1.395-1.525, so the
thresholds sit in gaps of 0.52 and 0.28 rather than being tuned to a boundary.

**It must be measured on one slim reference build, not per body type.** The separation exists on
every build but the thresholds move: on the Fat build every garment measures about as wide as the
body, putting Robe (1.008, wants fitting) and Jacket (1.026, does not) 0.018 apart.

### Two approaches that had to be discarded

**Uniform scanline remap.** Validated on ShirtBasic alone, which is fatally unrepresentative: a
tight shirt sits entirely inside the body outline, so the remap is harmless there. Against
Robe/Parka/Jacket it scored *below baseline* in 8 of 12 cases (Parka south s5: 0.941 → 0.811).

**Pixel-measured "tightness."** Median garment width over body width, meant to detect drape
automatically. Unusable across body types: on the Fat body every garment measures ≈1.0 (Parka
1.036) so everything read as skin-tight, while on Thin the same Parka read as fully loose
(1.463). It also collapsed to zero for vanilla-authored garments on SA's narrow bodies, which
is why an entire iteration silently did nothing at all. Apparel layer is coarser but stable,
and being a visible setting it is debuggable in a way a hidden heuristic is not.

Lesson for future changes here: validate against a tight garment **and** a draped one, on at
least two body types, and check for regressions rather than only a mean.

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
