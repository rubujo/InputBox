---
name: inputbox-web-dev
description: Engineering guidelines and accessibility standards for the InputBox project's web pages (gh-pages branch). Use this skill when modifying index.html, adjusting CSS, syncing localization, or optimizing web A11y.
---

# InputBox Web Engineering Guidelines

This skill provides the authoritative web engineering standards for the InputBox `gh-pages` branch. The project strictly enforces a "Zero-JS, Zero-Image" policy and requires compliance with WCAG 2.2 AAA standards.

## Core Reference Index

Load the relevant files based on your current task:

1.  **Web Architecture (Zero-JS/Zero-Image)**: `docs/engineering/web-architecture.md`
2.  **Web Localization (Radio Hack/Terminology)**: `docs/engineering/web-localization.md`
3.  **Web A11y & Safety (AAA)**: `docs/engineering/web-a11y-safety.md`
4.  **Web Style & Typography (CJK/kbd)**: `docs/engineering/web-style-typography.md`
5.  **Web Git & Validation**: `docs/engineering/web-git.md`

## Workflow Mandates

- **Interactions**: PROHIBITED from using JavaScript. All toggles must use the CSS sibling selector (`~`) and `:checked`.
- **Visuals**: Ensure "Zero-Jitter" during interactions (Hover/Focus).
- **Localization**: When modifying text, you MUST update all four languages (ZH, EN, JA, SC) simultaneously.
- **Validation**: Verify CJK symbol full-width conversion and `lang` attribute correctness before committing.
