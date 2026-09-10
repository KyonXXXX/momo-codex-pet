# Animation repair geometry investigation

- Two test runs reported an idle screenshot gap at fractional scale: 4 px at 80%, 6 px at 120%. The numerical window bound checks pass. No wall/drag checks reached yet.
- First hypothesis (one pixel of raster rounding) did not explain the 120% gap; stop widening tolerances.
- Resolved: logged pose union right = 183.33 logical px; the captured first pose is narrower, with shadow right = 180. At 120%, union padding plus the 2 px guard produces the observed 6 px gap. Test now compares rendered pixels with the current pose/shadow, while separately requiring exact clip-union clamping and exact character wall attachment. No runtime tolerance was widened.
- No data or credentials involved. Testing used isolated capture-mode instances. The verified 2.0.1 build is now installed and running from dist/Momo.
- Additional runtime finding resolved: a first movement tick smaller than one pixel can be rounded away by the native window. Collision completion now requires at least one logical pixel of attempted movement. Continuous wall climbing passed (624 to 609 in the bounded runtime test).
- Side-tail investigation: geometry-only changes left the tail inside a constrained Grid child layout slot. Screenshot pixel sampling found only shadow alpha at the expected triangle. Bubble overlay now uses Canvas, with explicit pixel assertions inside both side triangles.
