# Momo 2.1.0 maintenance handoff

- Phase: verified release candidate. Approved change: remove the concept's daily-suggestion text row; add only today's used/remaining percentages and progress bar. Existing weekly quota, credits, sync state and reset time remain visible.
- Implementation: Services/DailyUsage.cs persists observed weekly-percentage deltas by local date and weekly reset; PetWindow.DailyUsage.cs calculates/render daily progress; card is 160 x 124 px, adaptive layout resized accordingly. Pure reference budget = remaining weekly percent / max(1, remaining days).
- Coverage: seven new daily-usage test groups cover dynamic calculation, local date, restart, duplicates, missing/expired/out-of-order data, reset/correction, overspend, zero quota and final partial day. Packaged WPF regression: 391 checks passed. Pure/live checks: 22 passed. Evidence: artifacts/release-2.1.0.txt and artifacts/test-results.txt.
- Partial-day history cannot be fetched from the quota endpoint. First-start and overnight gaps show ≥ / ≤ markers, with details on hover. Records live in local daily-usage.json; no token or credits balance is stored.
- Original VPet assets, interactions, focus menu, cursor grip anchor and screen-edge behavior preserved. Static asset set: 609 groups / 6,181 frames.
- Limits and evidence: docs/validation-2.1.0.md; artifacts/daily-check.txt; artifacts/test-results.txt. Public UI captures contain synthetic data only.
- Next: finish packaged checks, install/start dist/Momo, publish v2.1.0 and verify uploaded ZIP.
