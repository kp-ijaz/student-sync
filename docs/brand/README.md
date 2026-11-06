# Student Sync brand kit

All files are extracted from `logo-original.png` (the approved logo, 2026-11). Each one is a
transparent PNG unless noted. Copies used by the apps live in
`mobile/student_sync/assets/brand/` and `src/StudentSync.Web/wwwroot/images/brand/`.

## Files

| File | Size | What it is |
|---|---|---|
| `mark.png` | 409 × 340 | The mark alone: open book (blue and purple pages) with the orange star |
| `mark_tile.png` | 512 × 512 | The mark on a white rounded tile |
| `app-icon-1024.png` | 1024 × 1024 | Opaque white app-store icon (source for iOS/Android launcher icons) |
| `wordmark.png` | 898 × 153 | "Student Sync": navy *Student*, purple *Sync* — for light backgrounds |
| `wordmark_light.png` | 898 × 153 | White *Student*, lilac *Sync* — for dark backgrounds |
| `lockup_horizontal.png` / `_light` | 1151 × 240 | Mark + wordmark side by side (light / dark backgrounds) |
| `lockup_stacked.png` / `_light` | — | Mark above the wordmark (light / dark backgrounds) |
| `book.png`, `star.png` | 409 × 340 | The mark's two layers on the same canvas; the loader turns the star over the book |
| `star_solo.png` | 120 × 120 | The star alone, trimmed (faint decoration only) |

## Where to use the mark alone

Places where the app's name is already shown by the platform, or there is no room for text:

- Launcher and app-store icons (iOS, Android adaptive and legacy), favicons, PWA icons.
- The loading indicator (`BrandSpinner` on web and mobile): book still, star turning a quarter
  and pulsing; still under reduced motion.
- Native launch screens (iOS storyboard, Android `launch_background`), on `#F6F7FE`.

## Where to use the mark with the wordmark

Places that introduce the product:

- Mobile: splash (spinner with the wordmark below), welcome screen header, sign-in and
  registration hero, the web-portal screen's app bar.
- Web: side menu header, sign-in and registration (hero and phone header).

## Light or dark background

- **Light** (white, `#F3F6FC`, `#F6F7FE`): `mark.png` + `wordmark.png`.
- **Dark** (navy sidebar, brand gradient): `mark_tile.png` + `wordmark_light.png`. The bare mark's
  dark-blue page disappears on navy, so it always sits on its white tile there.

## Rules

- Never recolour, stretch, rotate or add effects to the artwork; scale by height and let the width
  follow (mark 409:340, wordmark 898:153).
- Keep clear space of at least half the mark's height around it.
- Minimum size: mark 24 px tall; wordmark 14 px tall.
- Components: Flutter `BrandMark`, `BrandWordmark`, `BrandLockup`, `BrandSpinner`
  (`lib/core/widgets/brand.dart`); web `BrandMark`, `BrandLockup`, `BrandSpinner`
  (`Components/Shared`). Use them rather than referencing the PNGs directly.
