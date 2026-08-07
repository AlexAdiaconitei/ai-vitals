# AI Vitals — landing page (`gh-pages`)

Static landing page for [AI Vitals](https://github.com/AlexAdiaconitei/ai-vitals). No build step: plain HTML, CSS and JavaScript.

## Publish

GitHub repository → **Settings → Pages → Source: Deploy from a branch → `gh-pages` / `root`**.
The site is then served at `https://alexadiaconitei.github.io/ai-vitals/`.

## Contents

| Path | Purpose |
| --- | --- |
| `index.html` | Whole page, English copy inline as the default |
| `assets/css/site.css` | Tokens and layout, mirroring `DESIGN.md` (dark theme) |
| `assets/js/site.js` | English/Spanish dictionary, widget and screen switchers, ring meter, lightbox, copy buttons |
| `assets/img/` | Real application captures copied from `docs/images/`, app icon and brand SVGs |

## Release data

Download buttons point at `releases/latest/download/AIVitalsApp-win-{x64,arm64}-Setup.exe`, so GitHub resolves them to the newest published release — nothing to edit when you ship a version. Keep those asset names stable, and do not mark a release as pre-release, or `latest` will keep serving the previous one.

The tag and the installer sizes shown beside the buttons are read once from `api.github.com/repos/AlexAdiaconitei/ai-vitals/releases/latest`. If that call fails, the version-less copy stays (`latest`, `Always the newest published build`). Strings with `{version}`, `{x64}` or `{arm64}` are the live variants, chosen through `data-i18n-live` only when every value in them arrived.

## Language

English is the default. The EN/ES switch in the header rewrites every string from `I18N` in `assets/js/site.js` and stores the choice in `localStorage` under `aivitals.lang`. To edit copy, change both dictionaries in that file — the HTML text is only the English fallback for a no-JavaScript load.

## Updating captures

Copy the PNGs from the `main` branch and keep the file names:

```bash
git checkout main -- docs/images
mv docs/images/*.png assets/img/
```

Widget captures are shown at their real pixel size, so keep them unscaled.
