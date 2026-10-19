# Thumb wheel touch calibration (2026-10-20)

Recorded with the tray's TouchRecorder (trigger file `%LOCALAPPDATA%\Slender\touch-record.txt`): a 2-minute quiet session of
grabbing the mouse over and over, then 5 minutes of normal use with deliberate rests, taps and double taps. Features per touch:
duration, wheel counts, pointer travel during the touch, in the 400 ms before and in the 600 ms after, system idle time.

| | Touches | Pointer travel in the 400 ms before | Travel in the 600 ms after |
|---|---|---|---|
| Grabs | 35 | mostly 150-1500 px (some 0-30) | mostly 150-5000 px |
| Deliberate | 75 | 0-127 px, nearly all under 20 | 0-6 px (one 311) |

Rules now in the engine (SlenderEngine):

- Travel during the touch over 24 px (GrabMovePx): the hand is moving the mouse, no gesture.
- Travel over 150 px in the 400 ms before the touch (GrabBeforePx, GrabLookbackMs): a grab, no gesture until the thumb lifts.

Replayed on the recordings: 6 of the 7 grabs that still fired a gesture with the first rule are now ignored, and no deliberate
touch is lost. The one left is a still brush after 1.3 s idle, indistinguishable from a deliberate tap.

To recalibrate: write the trigger file (`label=grab`, `seconds=120`, `quiet=1`; then `label=normal`, `seconds=300`) and compare
the CSVs in `%LOCALAPPDATA%\Slender\calibration`.
