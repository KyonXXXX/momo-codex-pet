# Momo 2.0.1 maintenance handoff

- Phase: release complete. User outcome: mouse follows the raised clothing corner; bottom shortcuts removed and focus moved into right-click menu; quota bubble automatically moves inward at screen edges.
- Owners: Services/VPetCatalog.cs reads official mood anchors; PetWindow handles captured drag input; PetWindow.Layout.cs preserves sprite origin across bubble changes and attaches opaque character bounds to walls. Ground shadow is hidden during airborne actions.
- Native VPet touch/sleep remain in the full interaction system; the old bottom shortcut handlers are removed.
- Assets unchanged: pinned VPet 2e99a42ebeff71d792118f2e8de744b773042f8d, 67 families / 609 groups / 6,181 frames, 123 items, 13 activities, 12 layered recipes. Original PNGs remain in release packages rather than Git history.
- Verification: 388 packaged WPF checks passed, including render-content and side-tail pixel assertions. Pure/live checks: 15 passed. Evidence: artifacts/release-2.0.1.txt and artifacts/test-results.txt.
- Coverage: four raise moods, two sizes, three scales, capture loss, focus menu, quota toggle at walls/top/side hiding, native movement, feeding/activity settlement, touch regions and gallery tabs. Public captures use synthetic quota values only.
- Debug findings: clip-union bounds differ from the first frame; fractional native movement must not be mistaken for collision. See DEBUG_HANDOFF.md.
- Save compatibility and quota client unchanged. Limits: Windows WPF only; mixed-DPI cross-monitor behavior and physical audio output not exhaustively tested.
- Release artifacts: dist/release-v2.0.1 and dist/Momo-v2.0.1-windows-x64.zip. Next: observe ordinary desktop use, especially cross-monitor dragging on mixed DPI.
- Delivery verified: public GitHub v2.0.1 release contains Momo-v2.0.1-windows-x64.zip (507,286,087 bytes). Installed executable reports 2.0.1; its native window titled Momo · Codex 桌宠 is visible. Source implementation commit: 6742531c44a9804663e93771d2e67680dece1faa.
