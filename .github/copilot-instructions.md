# InputBox GitHub Pages Instructions (GitHub Copilot)

This branch utilizes **Agent Skills** to enforce web engineering standards.

## 0. Skill Activation
- **Trigger**: When modifying this branch, ensure the `inputbox-web-dev` skill is loaded.
- **Knowledge Base**: Reference the atomic guidelines in `docs/engineering/` before implementing changes.

## 1. Core Web Rules
- **ZERO JS / ZERO IMAGES / ZERO INLINE CSS**.
- **Localization**: All changes MUST update ZH, EN, JA, and SC languages simultaneously.
- **Symbol Rules**: CJK symbols must be full-width with no surrounding spaces.

## 2. A11y Standards
- WCAG 2.2 AAA (Contrast ≥ 7:1).
- No physical size changes on hover/focus (Zero-Jitter).
- Low frequency animations (≤ 1Hz).

## 3. References
- Atomic Web Standards: `docs/engineering/`
- Gemini CLI Instructions: `GEMINI.md`
