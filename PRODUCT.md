# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

Windows-first .NET MAUI app (unpackaged WinUI 3, `net10.0-windows10.0.19041.0`) that also ships to Android (`net10.0-android`). Same design language on both, but per-OS material capability: WinUI Acrylic/composition and a Win32 layered desktop-pet window on Windows, `RenderEffect` blur and edge-to-edge insets on Android. iOS is not a target.

## Users

Chinese-speaking people who keep an ongoing, emotionally continuous relationship with AI characters — on a desktop at home for long sessions, and on a phone for shorter check-ins. Secondary audience: the same people using it as a worldbuilding/writing companion (novel worlds, locations, character dossiers).

They arrive already attached: they want to resume a relationship, not evaluate a product. Reading a long thread, finding one remembered event, and tending a character's memory are the repeated jobs.

## Product Purpose

A local companion/writing app where each character keeps real, inspectable memory instead of a chat log: layered memory entries with a forgetting curve, tags and highlights, a relation network rendered as a living canvas, embedding-backed "sounds-like" recall, save-scoped history, and an on-desktop pet that speaks on its own. Success is the person believing the character actually remembers them, and being able to verify what it remembers.

## Positioning

Memory is a structure the user can open and look at — a graph of concepts, events, images and tags with visible connection strength and forgetting — while every model call goes to a endpoint the user chose. No neighbouring companion product exposes the memory itself as a browsable object, and none runs entirely on the user's own keys and local JSON files.

## Operating Context

Long desk sessions (chat, gallery, map/worldbook editors), a persistent desktop-pet window that must not read as a window, and phone sessions. Data is local JSON plus per-save memory files; the user can read the raw JSON of a relation net. Weather, calendar and location context feed the character's sense of "today". Four UI material worlds are a user-facing choice, and four colour palettes are a separate choice.

## Capabilities and Constraints

- .NET MAUI / .NET 10, CommunityToolkit.Mvvm, SkiaSharp canvases; no cloud backend, no telemetry.
- Three distinct AI model roles the UI must never conflate: 参数模型 (local parameter sliders), 量化模型 (a real embedding model at `POST {base}/embeddings`), 思考模型 (any configured chat model).
- Memory is per-save by default; cross-save is opt-in and needs character cloning.
- Keys and tokens go only to their own endpoint, never into shared `HttpClient` default headers.
- I18n in four languages; third person is gender-neutral (中文 TA / English they).
- Reduced-motion preference and a `WARM_NO_MOTION` kill switch are honoured; text must reach 4.5:1.
- The incumbent warm-neumorph look is **deprecated by explicit user instruction** (2026-10-18): four switchable material worlds replace it — 轻质感 (default), 柔和极简, 柔和渐变, 液态玻璃 — each with a dark variant. Colour palettes (经典奶油 / 晨雾蓝灰 / 翠竹绿 / 樱花粉) stay as an independent hue axis.
- No new Settings surfaces: the four worlds bind to the existing appearance picker.

## Brand Commitments

Name  WarmAsBefore. Voice: tender, plain-spoken, never cutesy; the warmth is in remembering, not in decoration. Four hand-tuned colour palettes already shipped and must keep working as the hue axis. Chinese-first copy.

## Evidence on Hand

Real seeded character data and a live memory vault (≈58 KB of human-memory JSON) in the app data directory; relation networks built from actual conversation turns; authored character sprites/portraits and background art in the asset folders; a working weather provider. Absences future work must not fabricate: no user counts, no testimonials, no benchmarks, no published release claims beyond `RELEASE_NOTES.md`.

## Product Principles

1. Memory the user can inspect beats memory the user must trust.
2. Continuity is the feature: everything else is packaging.
3. Warmth never costs legibility — a tender surface still passes contrast.
4. The user's models, keys and files; the app is a guest.
5. The character is a person in a place, with a today.

## Accessibility & Inclusion

Text ≥4.5:1 (large text ≥3:1) on every world and palette combination, including placeholder and secondary copy; dark theme is a first-class scheme, not an inversion; system reduced-motion honoured with an instant fallback; touch targets ≥48dp on Android; text scales with the OS font-size setting.
