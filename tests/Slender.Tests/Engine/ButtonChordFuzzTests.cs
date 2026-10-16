using Slender.Engine;

namespace Slender.Tests.Engine;

/// <summary>
/// Whatever came before (presses, releases, lost releases, timeouts, moves, ring resets, on/off), clicks must work again
/// within one click: the second plain left click after any history reaches the app as a press and a release. Clicks
/// stuck swallowed locked every window on 2026-10-16.
/// </summary>
public class ButtonChordFuzzTests
{
    [Fact]
    public void After_any_history_the_second_click_always_reaches_the_app()
    {
        var rng = new Random(20261016);
        for (var run = 0; run < 3000; run++)
        {
            var timer = new FakeHoldTimer();
            var app = new List<(ChordButton Button, bool Down)>();
            var chord = new ButtonChord((b, d) => app.Add((b, d)), timer);

            void Real(ChordButton b, bool down)
            {
                if (!chord.OnButton(b, down, 0, 0)) app.Add((b, down)); // passed straight to the app
            }

            var steps = rng.Next(1, 25);
            for (var i = 0; i < steps; i++)
            {
                var b = rng.Next(2) == 0 ? ChordButton.Left : ChordButton.Right;
                switch (rng.Next(8))
                {
                    case 0: case 1: Real(b, true); break;
                    case 2: case 3: Real(b, false); break;
                    case 4: timer.Elapse(); break;
                    case 5: chord.OnMove(rng.Next(-20, 20), rng.Next(-20, 20)); break;
                    case 6: chord.Reset(); break;
                    case 7: chord.Enabled = rng.Next(2) == 0; break;
                }
            }

            chord.Enabled = true;
            timer.Elapse();
            Real(ChordButton.Left, true); // the first click may pay off one lost release
            Real(ChordButton.Left, false);
            timer.Elapse();

            app.Clear();
            Real(ChordButton.Left, true);
            Real(ChordButton.Left, false);
            timer.Elapse();

            Assert.True(app.Contains((ChordButton.Left, true)) && app.Contains((ChordButton.Left, false)),
                $"run {run}: second click did not reach the app ({string.Join(", ", app)})");
            var down = app.IndexOf((ChordButton.Left, true));
            Assert.True(app.LastIndexOf((ChordButton.Left, false)) > down, $"run {run}: release came before the press");
        }
    }
}
