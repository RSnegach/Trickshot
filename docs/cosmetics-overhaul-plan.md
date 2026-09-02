# Cosmetics overhaul — execution plan

Scope: every generated hairstyle, facial hair and accessory for humans, horses and elephants.
Goal: production-ready quality — each item must read instantly as the real object, sit correctly
on the primitive anatomy, and have believable proportion, material and silhouette.

Companion document: `docs/cosmetics-verdicts.md` holds the full per-item plan for all 97 items
(what is wrong, the exact construction, code touchpoints, risks). This file is the execution
order and the decisions that span items. **Read the verdicts file for the item you are building
before you build it** — it carries measured numbers that were checked against the code and the
renders, and several correct plans are counter-intuitive.

## How the verdicts were reached

95 items were built on a real ragdoll and photographed from 4–5 angles (441 images) by
`Assets/Scripts/DevTools/CosmeticGallery.cs`, an editor-only tool that stays in the repo. An art
pass graded the renders; a second pass re-derived each plan against the actual code and
overrode anything infeasible. Where the two disagreed, the verdicts file records what changed
and why.

Verdict counts:

| Verdict | Count |
|---|---|
| Regenerate procedurally | 79 |
| Replace with a downloaded model | 6 |
| Keep as-is | 5 |
| Remove from the catalog | 7 |

Effort: 43 small, 44 medium, 10 large.

The download count is low on purpose. A bought-in mesh is only better when the object is a rigid
shell with non-axisymmetric creases that a lathe cannot express — a fedora's teardrop crown, a
cattleman crease, a welding hood. Everything that must *conform* to the body (beards, markings,
straps on a box muzzle, ears, anything tiny) is better generated, because a downloaded mesh is
modelled for a real head and will float or intersect on a 0.19 m sphere and cannot follow the
girth slider.

## Ground rules

1. **Style indices are wire state.** `PlayerAppearance` carries three ints replicated by index
   only. Appending to a catalog is safe; inserting or reordering silently repaints every saved
   profile. Removals shift indices — that is accepted precedent (commit `5fdec54` dropped 21
   accessories the same way) but note it in the commit.
2. **Cosmetics never get a collider.** `AttachAppearance`'s contract. Note `Make.Cylinder` and
   `Make.Capsule` *do* attach colliders — use `MeshGen` through a `Piece` helper instead.
3. **Solid decor that is also a hitbox must not move.** Elephant ears and tusks are header
   surfaces. Replace the visual mesh; leave the collider geometry exactly where it is. The ear
   rule is specifically that the outer face stays at x = 0.24 girth on every style.
4. **Everything scales.** Head cosmetics multiply literals by `_cosScale`; animal decor scales on
   girth or height depending on the surface it sits against. A piece placed by a height-scaled
   offset against a girth-scaled surface drifts off the body as the weight slider moves.
5. **Runtime meshes need `GeneratedMeshOwner`** or they leak — the customize preview rebuilds the
   body on every drag frame.
6. **Keep files CRLF.** `core.autocrlf=true`.

## Already built and verified

These landed during the review and are on disk, untracked. `git add` them.

- **`Assets/Scripts/Sim/MeshGen.cs`** — the geometry toolkit. `Param` (parametric surface with
  analytic normals), `Lathe`, `Cylinder`, `Torus` (full or arc), `Tube` (swept, per-node radius,
  parallel-transport frames), `Spline`, `Disc`, `Extrude` (ear-clipping, bevel, bulge),
  `Superellipse`, `SmoothOutline`, `Combine`, `Transform`, `Flat`, `Basis`.
  Two bugs were found by rendering and fixed: normals came back zero because the finite-difference
  cross product fell under `Vector3.Normalize`'s epsilon (everything drew black), and the extrude
  side walls were wound inside out. Verified output: a brimmed hat, a ring and open arc, a tapered
  curved tusk, a bevelled cupped ear, a cone and cylinder.
  **Do not rewrite these helpers** — two review groups independently asked for a `SweptTaper` /
  `Lathe` / `Torus` that already exist here.
- **`Assets/Scripts/Sim/CosmeticMesh.cs`** — mounts a downloaded model on the head bone: uniform
  scale from measured bounds against a chosen axis, `Centre`/`Bottom`/`Top` anchoring, material
  slots rebound by name so the accessory colour lands on the right part and lenses keep their own.
  Verified live: top hat, cowboy hat, fedora and round glasses all sit correctly on a sphere head.
- **`Assets/Scripts/DevTools/CosmeticGallery.cs`** — the capture harness, plus `SnapObject` for
  photographing a single test mesh. Editor-only (`#if UNITY_EDITOR`).
- **`PropKit`** — `Recolor` and a new `TryMeasure` wrapper were made public for `CosmeticMesh`.
  Note `PropKit.Place` is **not** usable for a head cosmetic: it sets `isStatic` and grounds the
  model. Use `Load` + `Instantiate` + `Recolor`, which is what `CosmeticMesh` does.

Compile check passes (`dotnet csc` over the whole runtime assembly — see CLAUDE.md).

## Assets acquired

Downloaded, license-checked, converted to Unity-ready OBJ, and imported under
`Assets/Resources/Cosmetics/_probe/`. The probe folder is a staging area — promote the chosen
models to a permanent path and delete the rest before committing.

| Source | What | License |
|---|---|---|
| poly.pizza | 67 models: hats, 25 eyewear variants, masks, pipe, cigar, lollipop, earrings, necklaces, saddle | CC0 and CC-BY 3.0, recorded per model |
| Kenney Prototype Kit | `hat-cap` (real baseball cap, 64 tris), hard hat | CC0 |
| Kenney Mini Characters | `aid-glasses`, `aid-sunglasses`, `aid-mask` | CC0 |
| Kenney Food Kit | `lollypop`, chopstick | CC0 |
| KayKit Adventurers | wizard hat (fused to the Mage mesh, needs splitting) | CC0 |
| OpenGameArt / OwlishMedia | 85 hair-clump alpha PNGs, 2048² | CC0 |

`Assets/Resources/Cosmetics/manifest.json` records id, title, author, license and source URL for
every downloaded model. **Every CC-BY item needs a credit line** in a shipped credits file before
release — Poly-by-Google-era models are CC-BY, the iPoly3D / hat_my_guy / Quaternius / Kenney ones
are CC0. Prefer the CC0 option where quality is equal.

Nothing usable was found for: horse bridle or halter as a separate mesh, elephant caparison,
bat-style cowl, hockey mask, Venetian mask, bindi, septum ring, eyebrow barbell, vampire fangs, or
any stylised hair mesh. Those are all regenerate verdicts anyway.

## Build order

Infrastructure first — most items are one call once their helper exists. Each phase ends with a
gallery recapture of just that group, so a regression is caught against the before-shot.

### Phase 1 — shared infrastructure

Roughly in dependency order. All in `Cosmetics.cs` unless noted.

- **`Piece(parent, mesh, mat, pos, rot, scale)`** — wraps a generated mesh in a collider-less child
  with `GeneratedMeshOwner`. The bridge between `MeshGen` and the cosmetic catalogs.
- **`Own(Material)`** + a `_rag` scratch field set in `AttachAppearance` — registers a material for
  teardown. `Glass()` and `Dark()` currently leak one material per build, by their own comment.
- **`Make.Transparent(...)`** and a `Lens(...)` wrapper — premultiplied-alpha Standard for glass.
  Do **not** change `Glass()`: it has 17 callers relying on it being opaque.
- **`SurfacePatch(head, mat, dir, outline, standoff)`** — a polar fan on the sphere taking an
  arbitrary outline in tangent-plane metres. Replaces `HeadPatch`'s rectangular grid (whose only
  caller is the eyepatch, so the signature is free).
- **Outline generators** — `Circle`, `RoundedPoly`, `Teardrop`; all star-shaped so a fan
  triangulates them.
- **`SweptTube(head, mat, dirs, standoff, rW, rH, sides, closed)`** — a tube that follows the
  sphere surface. Glasses rims, temples, straps, cords, chains. A closed one on a spherical circle
  is the monocle's ring.
- **`FaceShell(head, mat, cols, rows, P, uv)`** — parametric surface grid for beards and masks.
  **Normals must be finite differences of P**: `EmitHeadGrid` emits both windings over the same
  verts, so `RecalculateNormals` averages them to zero.
- **`FaceMap` constants + `SeamLat(az)`** — see "the chest seam" below.
- **`HairShell`** — the shaped scalp cap with a real hairline, replacing `CrownPatch`.
- **HairSim additions** — `RootMode.Hairline`, `RootMode.TieCluster`, `RootMode.Path` (with
  `rootPath` / `rootNormals` / `rootSpread` / `rootSideBias`), `AddCapsule` collision, a hug clamp,
  a bundle constraint, and `rootRadius` so roots sit on a shell.
- **Tuft atlas** — a code-generated `Texture2D` of tapered strand clumps, from the OwlishMedia
  alphas or painted procedurally. See "the atlas" below.
- **`MeshGen` additions** — `FanSheet` (elephant ears), `SphereCap`, `Ribbon` (straps and hems).
- **`AccessoryEntry.Smoothness` / `.Metallic`** per entry, replacing the hardcoded 0.25 at the
  material creation site. Use metallic 0.75, not 0.9: the gallery renders with flat ambient and no
  reflection source, so 0.9 reads near-black in the sheets even though the game is fine.
- **Gallery**: add a reflection probe or skybox reflection so metals can be judged in recaptures.

### Phase 2 — human head (the most-seen surface)

Hair → facial hair → eyewear → hats → masks → props and jewellery. Hair first because `HairShell`
and the tuft atlas unblock the most items and set the quality bar.

### Phase 3 — animals

Horse mane (a catalog split, see below) → horse markings and tack → elephant ears, tusks, tack.

### Phase 4 — verification

Recapture all 95 items, diff against the before-sheets, fix regressions, compile check,
`graphify update .`, commit with credits and license files.

## The cross-cutting findings

These are the things that explain many items at once. Each is measured, not asserted.

### The atlas and why short hair looks like cardboard

`HairAtlas.png` is four full-height strands. Measured coverage at the 0.4 cutoff: strips 0–1 do
feather at the tip, strips 2–3 stay 47–49% opaque then cut hard. But the feather lives in the last
8% of V, which is 6 mm on a 7.5 cm card — so short cards are square-ended in practice.

The dominant cause is worse than the tip: a short style maps the *whole* V range onto 2 quads, so
the mip chain averages the painted strand lines into a uniform value above the cutoff and the card
resolves to a filled rectangle. Card widths of 5–8 cm are 26–42% of the head radius, and with 3
nodes they cannot bend along or across.

Fix: **do not edit the PNG** — that keeps the two KEEP styles bit-identical. Add a second
code-generated tuft texture of tapered clumps, extend `HairDef` with an atlas selector and
`vRoot`/`vTip`, and let short styles use 1.5–2.5 cm cards with 4–5 nodes. One `.meta` change is
worth making: `mipMapsPreserveCoverage 1`, `alphaTestReferenceValue 0.4`, which stops distant
cards eroding to threads and helps Long and Shoulder Length too.

### CrownPatch reads as a bowl cut

The shared scalp cap ends in a perfect horizontal circle just above the ears, so Fringe, Ponytail
and Man Bun read as a helmet, and the back view shows a vertical seam with a left/right shading
step. Replace it with `HairShell`: a loft bounded by a real parametric hairline (forehead 0.85 rad,
temple notches 0.75, sides 1.25, behind the ear 1.40, nape 1.55), closed with a rounded lip so it
has visible thickness, with vertex tangents pointing along the comb direction so the anisotropic
sheen follows the style.

### The chest seam (measured, and it invalidates the obvious beard plan)

The head sphere sinks 4–5 cm into the torso box. At azimuth 0 the sphere is buried below latitude
−54.6°, and the clip latitude swings with build — from −41° at girth 1.2 to −81° at girth 0.8.
So "under the chin" is *inside the chest* and invisible. Every lower outline must clamp to
`SeamLat(az) + 1°`, and beard bulk must sit in front of the chest, never under the sphere. The
chin point is at latitude −50°, not the −58° the art pass assumed.

### The horse mane is the wrong catalog

The mane reuses the human hair catalog tilted onto the neck crest. Crown, Ring and FrontSweep root
modes scatter over the whole sphere, so on a horse they sprout from the muzzle and both cheeks —
the Afro sheet shows hair clipping through the face. Split it: give the horse its own list
(Roached, Short, Standing, Flowing, Long Flowing, Braids, Forelock) driven by a new
`RootMode.Path` that roots strands along the actual crest polyline, with capsule collision against
the neck so cards do not pass through it.

Six human styles are removed from the horse list. `SpeciesCosmetics.UsesHumanHair` goes away for
the mane slot, and the crest polyline must be derived from `D_Neck`'s real numbers — hoist those to
constants so the geometry and the hair cannot drift, replacing the current "keep these two numbers
equal" comment.

### Trademark

Rename "Batman Mask" to a generic cowl before shipping. The plan calls it "Vigilante Cowl". Avoid
the Guy Fawkes face that turned up in asset research for the same reason.

### Removals

Seven items: six human hair styles dropped from the *horse* list only (the human entries are
untouched), and Nipple Piercings. The latter is parented to the head bone with fixed offsets, so
it slides ~10 cm across the chest on a 30° yaw, and the torso is the jersey box so it cannot read
over a shirt anyway. If a bare-torso variant is ever wanted, it needs a torso-parented build path.

## Decisions (answered — these are settled, build to them)

### 1. Credits screen, and Options becomes Settings

Two linked pieces of work, both approved.

**Rename Options to Settings everywhere.** Scope is small and fully enumerated: the class
`OptionsMenu` (`Assets/Scripts/Play/OptionsMenu.cs`) becomes `SettingsMenu` with the file renamed
to match; 13 literal occurrences of `Options`/`OPTIONS`/`OptionsMenu` across
`MenuUI.cs`, `PauseMenu.cs`, `GameInput.cs`, `Keybinds.cs`, `CareerStatsUI.cs`, `GameCamera.cs`,
`PauseMatchSetup.cs`, `DisplaySettings.cs`, `GameBootstrap.cs`. The two user-visible strings are:

- The **hub button in the top-right of the second menu screen** — `MenuUI.cs:125`, drawn in
  `DrawHub()` at `Rect(MenuScale.Width - marginX - 90f, 26f, 90f, 34f)` with the label `"OPTIONS"`.
  Becomes `"SETTINGS"`. **Widen the button**: it is a fixed 90 px and the new label is two
  characters longer, so it will clip at 12 pt bold. Use ~110 px and shift the x origin by the same
  amount to keep it flush to the right margin. Check it at a small window size, where `MenuScale`
  shrinks the canvas.
- The pause-menu entry `"Options"` — `PauseMenu.cs:227`, becomes `"Settings"`.

Also update the doc comments that describe the panel (including the `MenuUI` class summary at
`MenuUI.cs:15`, which lists the hub cards), the `hasOptions` / `_optionsOpen` / `_options` locals
and fields, and the CLAUDE.md mention if one exists. Renaming the internals is optional but keep it
consistent either way.

**Add a Credits button inside Settings.** The panel already has a tab strip
(`enum Tab { Keybindings, Audio, Quickchat, Camera }`); add a fifth tab, `Credits`. Note the panel
is reachable from *both* the main-menu hub and the pause menu, so the credits are available from
either — no extra wiring needed.

Content and behaviour:

- **Rolling, looping credits.** A vertical scroll that runs on its own and wraps back to the top,
  not a static list. Advance a scroll offset by `Time.unscaledDeltaTime` (unscaled, so it still
  rolls while the game is paused) and modulo it against the total content height. Allow the mouse
  wheel or a drag to scrub, and pause the auto-scroll briefly after the user interacts. Draw inside
  the existing `MenuScale` block and lay out against `MenuScale.Width/Height` like the rest of the
  panel.
- **Order:** creator first, then an assets section beneath it.
  - **Created by — Roman Snegach.**
  - **Assets** — its own section below, listing every third-party asset that requires credit.
- Source the asset entries from `Assets/Resources/Cosmetics/LICENSES.md` (the CC-BY half) plus the
  existing licence files already in the project: `Assets/Resources/Audio/Audio-License.txt`,
  `Fonts/Fonts-License.txt`, `Hair/HairAtlas-License.txt`, `Sky/Sky-License.txt`,
  `Turf/Turf-License.txt`, and the Kenney prop kits. Give each entry the asset name, the author,
  and the licence.
- CC0 assets do not legally need crediting, but list the packs anyway (Kenney, Quaternius, KayKit,
  OwlishMedia) — it costs nothing and it is the norm.
- Keep the credit text in one place in code (a `string[]` or a small structured table) so adding an
  asset later is a one-line edit rather than a layout change.

This unblocks the CC-BY download items: all six may use their downloaded model.

### 2. Removals approved

Deleting Nipple Piercings is approved, including the index shift for Cigar through Wizard Hat on
existing saves. Same handling as the earlier removal in commit `5fdec54`: no migration, the clamp
and bounds-check absorb it. Mention the shift in the commit message.

### 3. Scope — groundwork first, then human head

Confirmed approach. "Shared groundwork" is Phase 1 above: the dozen or so reusable helpers
(`Piece`, `Own`, `SurfacePatch`, `SweptTube`, `FaceShell`, `HairShell`, the HairSim root modes, the
tuft atlas, the `MeshGen` additions). They are worth building first because most individual items
collapse to a single call once they exist — the tube helper alone serves glasses arms, straps,
chains, the pipe stem and the horse bridle. Skipping it produces a dozen one-off implementations
that each need fixing later.

**First slice: Phase 1, then human hair and facial hair** (21 items). That is where the eye goes
and where the worst offenders are, and it produces a visible result early enough to judge the
approach before committing to eyewear, hats, masks, props and the animals.

## Status (2026-09-02): executed

All four phases are built, rendered and committed. Every one of the 95 items was recaptured after
its slice landed and reviewed against its before-sheet; the after-sheets live next to the before
ones. Commits, in order: shared groundwork + human hair and facial hair, human accessories,
horse (`0923a0e`), elephant, then this verification pass.

What changed against the plan while building it:

- The horse markings and tack, and the elephant tack, are a **Cosmetics pass over the built
  decor** (`ActiveRagdoll.TryGetDecor` hands back each decor piece's transform and scaled dims)
  rather than a `DecorVisual` enum on the table. Gated primitive rows for them are gone; the
  option lists and indices are unchanged.
- Elephant ears and tusks keep their solid rows as **Hidden colliders** (`DecorSpec.Hidden`), so
  the hitbox is still the table while the visual is a fan sheet / swept tube. Tusk rows are
  `GirthDims` chords of one shared arc (`BodyLayout.ElephantTuskArc`), so the whole tusk is
  outside the skull at every build; the Banded rows and the tack rows were deleted.
- Elephant ear colour is the hide colour, not the StyleA tint: an ear that is not the colour of
  the head it grows from reads as a prop. Slot colours are seeded per species on a species change
  (`SpeciesCosmetics.SeedStyleColors`) so a fresh elephant gets ivory tusks and red tack.
- Dapples are painted as translucent soft blotches in a lighter shade of the coat; the blaze runs
  as one strip from the forehead over the muzzle top to between the nostrils.

## Where things are

| Path | What |
|---|---|
| `docs/cosmetics-verdicts.md` | Full per-item plan, all 97 items, with touchpoints and risks |
| `docs/cosmetics-before/sheets/` | The 95 before-renders, one labelled contact sheet per item |
| `docs/cosmetics-after/sheets/` | The 95 after-renders, same names, for side-by-side comparison |
| `Assets/Scripts/Sim/Cosmetics.Horse.cs` / `.HorseDecor.cs` | Mane catalog; painted markings and lofted tack |
| `Assets/Scripts/Sim/Cosmetics.Elephant.cs` | Fan-sheet ears, swept tusks, head cloth, ankle bands, blanket |
| `docs/cosmetics-before/sheet.py` | Rebuilds contact sheets from raw gallery PNGs |
| `Assets/Scripts/Sim/MeshGen.cs` | Geometry toolkit (Param, Lathe, Tube, Extrude, Torus, Combine...) |
| `Assets/Scripts/Sim/CosmeticMesh.cs` | Downloaded-model mounting for the hat models |
| `Assets/Scripts/DevTools/CosmeticGallery.cs` | Capture harness, editor-only |
| `../cosmetics-probe-staging/_probe/` | The 150 candidate model files, moved OUT of the project (Resources ship in builds); the five keepers live in `Models/` |
| `Assets/Resources/Cosmetics/manifest.json` | id, title, author, license, source URL per model |
| `Assets/Resources/Cosmetics/LICENSES.md` | Credit list split by CC0 / CC-BY |

Recapture the gallery with, in play mode:

```
Trickshot.CosmeticGallery.Begin(@"<out-dir>", "");            // everything
Trickshot.CosmeticGallery.Begin(@"<out-dir>", "human_hair");  // one group
python docs/cosmetics-before/sheet.py <out-dir> <sheet-dir> 400
```
