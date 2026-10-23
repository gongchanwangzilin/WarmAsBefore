---
version: 1
slug: "views-maingamepage-xaml"
primary_target: "Views/MainGamePage.xaml"
related_targets: ["Views/CharacterLibraryPage.xaml","Views/MemoryGraphPage.xaml","Views/MemorySearchPage.xaml"]
---

# Surface brief — main chat surface

Scope: the chat surface (`Views/MainGamePage.xaml`) plus the browse surfaces it reaches
(`Views/CharacterLibraryPage.xaml`, `Views/MemoryGraphPage.xaml`, `Views/MemorySearchPage.xaml`).
Mode: Operate. Platform: adaptive (WinUI 3 unpackaged + Android).

User-pinned constraints that outrank the roll: four switchable material worlds —
轻质感 (default), 柔和极简, 柔和渐变, 液态玻璃 — each with a first-class dark variant;
the four colour palettes (经典奶油 / 晨雾蓝灰 / 翠竹绿 / 樱花粉) stay an independent hue axis;
the incumbent warm-neumorph look is deprecated; no new Settings surface (the worlds bind to
the appearance picker that already exists).

## Direction contract

THESIS: a memory the user can pull out of a drawer and read both sides of — refuses the
app-default arrangement of one chat column of identically-dressed bubbles over a list of
cover-art characters.

OWN-WORLD: an index-card drawer and a study notebook. Every surface is a card with a rule
under its head, a hanging label, and a right-aligned date pressed tight to the margin;
references between entries are printed as cross-reference marks («见 雨天»), not as
navigation chrome. Colour lives in narrow bands — an emotion strip, a tag tick, a save
side — never as a wash. Four materials (laminate / matte-flat / low-chroma duotone /
glass), four hues, two grounds.

STORY: the visitor sees who is speaking, what TA remembers of this, how strongly, and how
long ago, then acts (reply, jump into the net, search by feel) without leaving the column.

FIRST VIEWPORT: the dialogue column owns the field, flush to the ground — no page padding.
Above it a running head names the span you are inside (最早的一条 · 最近的一条) and recomputes
as you scroll. The active character sits in a tab divider at the head with a hairline seam
carrying its emotion strip. Replies are cards with a hanging name and a right-aligned time;
the composer is a single ruled slot at the foot, primary action as one pill. Reading a
message drops the rest of the column to a faint pattern (one field, one focus).

FORM: grounded candidate 6 of 7 — index-card drawer / notebook cross-reference; seed key
d1e4e8dc, assigned index 6, mode operate.

RAISES (donor → discipline kept, not clothing):
- dark-first developer console — 缝，不是影: dense lists separate on 1px seams and one tonal
  step; shadow is reserved for "act on this now". Destructive actions get deliberate air.
- rw-lexicon-shoulder-and-plate — 眉栏引导词: the running head names first/last entry in view,
  recomputed from scroll; colour quarantined inside a framed plate.
- brand-identity-canon-saville-catalog-sleeve — 情绪色条: composite emotion reads as a coded
  strip of hue blocks that decodes to words only on focus; nothing is labelled twice.
- vernacular-ephemera-cassette-j-card — 两面，不是标签页: per-save vs cross-save memory are
  hard halves of one card with a real flip, running times right-aligned to the margin.
- ikeda-datamatics — 一域一焦: focusing one entry dims the whole field to a faint pattern;
  under reduced motion it holds a still instead of strobing.
- pop-culture-shelf-streaming-title-card-wall (competitive, kept) — 焦点行升起、邻位让路:
  in the character drawer, the focused card expands and reveals its metadata while its
  neighbours slide back and the rest of the shelf loses contrast.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review,
the verdict, DESIGN.md, and every shipping raster carrying its provenance.

## Open decisions

Default world is 轻质感; whether 液态玻璃 may fall back to opaque scrim where the OS cannot
blur (Android < 31, low-end Windows) is resolved in code by the reduced-transparency switch.
