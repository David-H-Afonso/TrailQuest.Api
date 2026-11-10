# Adventure authoring / Crear aventuras

## The contract

Adventures are authored manually in source control. There is no runtime generation, content schedule or remote catalog. Create a versioned pack, verify its non-spoiler summary, media rights and validation, then include it in a normal release.

```text
Content/
  Schemas/adventure.schema.json
  Adventures/<stable-id>/
    adventure.json
    SOURCES.md
    assets/                 local SVG / WebP / JPEG / PNG
  Challenges/challenges.json
  Demo/sample.json          optional synthetic activity, NOT adventure definitions
```

The API discovers `adventure.json` files on startup. A new adventure must not require adding a React import, a route-ID branch, another controller or a database seed. Restart to pick up source-content changes; nothing watches or publishes content in the background.

## Fields and semantics

- `schemaVersion: 1`; `contentVersion: "1.0.0"`; stable lowercase `id` matching its directory.
- `name`, `subtitle`, `description`: public spoiler-free presentation. `introduction` explains the premise and whether distance is symbolic.
- `setting`: `real` or `fiction`. This is independent of `map.kind` (`geographic`, `schematic`, `illustrated`). `kind` remains the compatible visual-category field (`city` / `fantasy`), not a route identity.
- `passport`: title, subtitle, region label, six-digit accent color, Lucide icon key, completion text. Changing a country must only change content.
- `coordinates`: geographic `[latitude,longitude]`; illustrated `[x,y]` on a 1000×540 canvas.
- `distanceKm`: virtual walking challenge target, **not automatically a verified real walking distance**.
- `checkpoints`: stable ID, display/local names, threshold in km, position, category, short description, fact, `story` paragraphs, stamp icon and optional gallery.
- `checkpoints[].image`: public, locally stored place-photo preview, including for locked stages. Earned `story`/`gallery` remain gated. Real-location prose describes the real place; do not write an imaginary first-person visit or repeat interface mechanics in every paragraph.
- Media may provide `sourceUrl` and `licenseUrl` (public HTTPS only). Credits are available directly from the place sheet. Real destinations should use verified real photographs, not generic city illustrations or generated images presented as photos.
- Thresholds must be sorted, start at 0, end at the route target, and refer to ordered coordinates. Do not invent a safe street route: use a labelled schematic for symbolic regional itineraries.
- `cover` and gallery `file` values are plain filenames under this pack's `assets/`; no remote image URLs or traversal. Gallery items require meaningful alt text, caption, author credit and license.
- `references`: HTTPS source links; `SOURCES.md` records exactly which facts/media were checked, licenses and modifications.

Renderers escape text; content does not contain executable scripts, arbitrary formulas or HTML. Public catalog responses omit checkpoint narratives; authenticated journey responses reveal earned chapters only. Source files themselves necessarily contain the complete story.

## Versioning and preservation

Starting an adventure stores a complete definition snapshot and content version in its journey. Existing pre-expansion journeys receive their baseline snapshot during the additive migration/startup, without replacing daily history, start dates, visits or achievements.

New starts use the installed definition; existing starts keep their snapshot. **Never reuse IDs for a different meaning.** For an incompatible new adventure use a new ID. For editorial changes bump the content version. Keep old media files under versioned filenames (`cover-v1.svg`, `cover-v2.svg`) rather than overwriting them; snapshots may still reference the old file. Keep historical pack directories/assets installed when retiring a destination. Removing source assets is not the same as removing disposable demo fixtures.

Compatible editorial updates can be explicitly applied with **Actualizar ficha** or `PUT /api/v1/profiles/{profile}/journeys/{routeId}/content`. The server compares route distance, map kind, geometry and every checkpoint ID/threshold/position before replacing only the content snapshot/version. An incompatible update returns 409. History, start dates and earned rewards are retained; personal editions are never silently upgraded.

The initial packs are Tokyo, original-fiction Brumavalle and a Hokkaidō regional itinerary. Hokkaidō demonstrates a third destination with a real, locally stored CC0 photograph and a keyboard-operated gallery. Illustrations are labelled as illustrations, not photos or actual user memories.

## Prompt template

> Create a TrailQuest adventure pack for [place / original fictional world]. Use [distance] of virtual walking, [number] chapters, [tone]. I want to discover the narrative while progressing, so do not spoil it in the final summary. Use the existing schema, generic renderers, unique stable IDs, locally stored rights-cleared media, citations for real facts, and explicit map/distance limitations. Do not publish, schedule, commit or deploy. Preserve existing passports and validate the pack and UI.

If “Santiago” is ambiguous, ask which destination before authoring facts or geometry.

## Strength challenges

`Challenges/challenges.json` contains reusable declarative targets: ID, name, description, metric, unit, target and comparison note. Supported metrics currently are `strength-volume`, `strength-repetitions` and `strength-sessions`. They use performed activity records, not planned workout instructions. Walking adventures and strength challenges are distinct typed content; unsupported metric names are not evaluated as arbitrary code. More sport-specific metrics require a tested calculator before content can use them.

Volume is recorded repetitions × external load in kg, not work/energy and not proof of lifting an object in one attempt. Unknown/bodyweight loads stay unknown. Avoid extreme daily quotas, punitive streaks, medical claims and unlicensed franchise artwork.

## Validation

From the independent API repository run `dotnet test TrailQuest.Api.sln`. In the separate frontend repository run `npm run build`, `npm run lint` and `npm run test`; its synthetic contract fixtures do not import API files. Startup validates catalog IDs, versions, maps, ordered thresholds, finite coordinates, source links and media files. The JSON Schema is an additional editor/authoring contract; runtime validation is in `RouteCatalog.Validate`.

Browser checks use an isolated database and intercept tile providers; never pan/zoom live OpenStreetMap in automation. Check empty profiles, locked/unlocked chapters, gallery keyboard controls and mobile/tablet/desktop. Do not put personal screenshots or databases into the content pack.
