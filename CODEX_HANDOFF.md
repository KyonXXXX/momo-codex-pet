# Momo 2.1.1 daily progress repair

- Phase: release complete.
- User-reported symptom: daily row appeared stuck at ≥0% and ≤100%.
- Findings: partial-day state always added inequality markers; live reset timestamps also changed from 10:51:40 to 10:51:41, which the exact-equality cycle check treated as a new week and reset the daily baseline.
- Fix: display the recorded numeric percentages with up to two decimal places; explain partial coverage only in the tooltip. Accept up to 120 seconds of reset timestamp jitter while preserving daily increments and the original tracking start. Large window changes, local date changes, scope changes and decreasing used-percent still establish a new baseline.
- Tests added: partial-day numeric labels, zero, hundredths, over-budget numeric display, repeated jitter through restart and real reset.
- Existing settings are preserved. The old app had erased the observed 36→37 increment before replacement. A backed-up local repair restored one verified weekly percentage point using the same-window baseline captured during this session; no unobserved history was inferred.
- Verification: 366 packaged UI checks and 23 pure/live checks passed. Installed executable reports 2.1.1 after graceful replacement of the exact pet window. Live accessibility read verified 今日已用 8.06% and 剩余 91.94%; the restored record retained its original start after restart/sync. Evidence: artifacts/release-2.1.1.txt and artifacts/test-results.txt. Subsequent live refresh also preserved the recovered increment. Public v2.1.1 ZIP verified at 507,301,558 bytes. Next: observe normal daily accumulation.
