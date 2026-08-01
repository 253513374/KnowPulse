# RAG 管理方案 1：设计 QA

## Comparison Target

- Source visual truth: `C:\Users\q4528\.codex\generated_images\019f8460-52d4-7a53-97c3-924029fe5fcb\exec-a759226f-a021-42b3-bd4f-5a580d821fb4.png`
- Final implementation screenshot: `C:\Users\q4528\.codex\visualizations\2026\07\21\019f8460-52d4-7a53-97c3-924029fe5fcb\rag-design-qa\04-answer-grounded-pass-2.png`
- Model-management screenshot: `C:\Users\q4528\.codex\visualizations\2026\07\21\019f8460-52d4-7a53-97c3-924029fe5fcb\rag-design-qa\02-model-bindings.png`
- Viewport: 1440 x 1024 desktop; additional checks at 768 x 900 and 390 x 844.
- State: grounded answer with three citations for the direct visual comparison. The local knowledge base has no published test content, so the grounded comparison state is available only through a `DEBUG`-only preview query; the production route still calls the real RAG service.

## Full-view Comparison Evidence

- Pass 1 comparison: `C:\Users\q4528\.codex\visualizations\2026\07\21\019f8460-52d4-7a53-97c3-924029fe5fcb\rag-design-qa\comparison-pass-1-full.png`
- Pass 2 comparison: `C:\Users\q4528\.codex\visualizations\2026\07\21\019f8460-52d4-7a53-97c3-924029fe5fcb\rag-design-qa\comparison-pass-2-full.png`
- The final screen preserves the source hierarchy: page title and configuration action, three-part readiness strip, user question, grounded assistant answer, evidence status, inline citation rows, and bottom composer.
- The final implementation fits the full desktop viewport without clipping persistent controls. The model-management surface uses the same visual language and separates usage bindings, the catalog, and health records into working sections.

## Focused Region Comparison Evidence

- Pass 1 focused comparison: `C:\Users\q4528\.codex\visualizations\2026\07\21\019f8460-52d4-7a53-97c3-924029fe5fcb\rag-design-qa\comparison-pass-1-focus.png`
- Pass 2 focused comparison: `C:\Users\q4528\.codex\visualizations\2026\07\21\019f8460-52d4-7a53-97c3-924029fe5fcb\rag-design-qa\comparison-pass-2-focus.png`
- The focused pass verified the answer line length, assistant branding, evidence rhythm, source metadata, source actions, composer controls, and spacing between the major regions.

## Required Fidelity Surfaces

- Fonts and typography: uses the existing KnowPulse/LeptonX sans-serif stack. Heading weight, 14-16 px body scale, answer line height, secondary metadata, and truncation hierarchy match the source intent. No actionable wrapping or truncation defects remain.
- Spacing and layout rhythm: desktop content order, card radii, dividers, inner padding, citation-row rhythm, and composer placement match the source. The first pass was too tall; the second pass removes the viewport clipping.
- Colors and visual tokens: reuses the existing blue primary and pink navigation accent, with green, amber, and red reserved for actual runtime states. The source shows green model states, while the implementation correctly shows `待检查` because the current local models have no health snapshot.
- Image quality and asset fidelity: the existing KnowPulse logo asset is used for the assistant identity. Font Awesome supplies the standard interface icons. There are no generated placeholders, custom SVGs, CSS drawings, decorative raster assets, or emoji substitutes.
- Copy and content: labels use the product language `RAG 管理`, `问答验证`, and `模型管理`; technical UUID, confidence, and Top K details are removed from the primary task. Recovery copy distinguishes missing evidence from a model-management action.

## Interaction And Accessibility Evidence

- Verified the empty question state, example-question fill, empty-submit validation, busy/disabled behavior in the implementation, real insufficient-evidence response, source recovery actions, model-section navigation, new-model editor, and model-form validation.
- Verified semantic headings, form labels, validation messages, status regions, `aria-live`, disabled states, and keyboard-reachable native controls.
- Browser console errors checked: none.
- Responsive captures: `05-answer-tablet.png`, `06-answer-mobile.png`, and `07-model-mobile.png`. No horizontal overflow, clipped primary action, or overlapping content was found.
- The local knowledge base currently has no published content, so a real successful grounded answer could not be produced without changing local business data. The real refusal path was verified against the running API; the `DEBUG`-only visual fixture is excluded from release builds.

## Comparison History

### Pass 1 — blocked

- [P2] The composer extended below the 1440 x 1024 viewport, hiding part of the persistent input controls.
- [P2] The assistant avatar used a generic icon instead of the real KnowPulse mark.
- [P2] The grounded preview omitted `查看原文`, making the source list less actionable than the selected visual.

Fixes made:

- Reduced answer-region padding and citation-row height, and tightened the composer without reducing readability.
- Replaced the generic assistant icon with the existing KnowPulse logo thumbnail.
- Added non-production preview citation identifiers so the visual QA state includes the same source action as a real result.

### Pass 2 — passed

- Post-fix evidence: `comparison-pass-2-full.png` and `comparison-pass-2-focus.png`.
- No actionable P0, P1, or P2 mismatches remain.
- Expected differences: live health states remain `待检查` instead of the mock's `已就绪`; the attachment affordance was intentionally omitted because RAG question attachments are not a supported product behavior.

## Follow-up Polish

- [P3] A later iteration can add conditional Enter-to-send once the textarea keyboard behavior is implemented without breaking Shift+Enter multiline input.

final result: passed
