using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Slender.Engine;
using Slender.Settings.Logic;
using Slender.Settings.Views;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace Slender.Settings;

/// <summary>
/// The 3CX page's Ring sound section (docs/3cx-ringer.md): which sound plays on the PC while a call rings, on which playback device and
/// how loud. The playback devices are read once when the page opens; nothing plays here, Play asks the tray.
/// </summary>
public sealed partial class MainWindow
{
    private const int RingSoundPlayLockoutSeconds = 3;

    private void BuildRingSoundSection()
    {
        HapticSettings Now() => _config.Config.Haptics;
        void Edit(Func<HapticSettings, HapticSettings> f) => _config.Update(x => x with { Haptics = f(x.Haptics) });

        AddSection("RING SOUND");
        var soundBox = new ComboBox { Width = 220 };
        AutomationProperties.SetName(soundBox, "Ring sound");
        foreach (string choice in RingSoundText.SoundChoices()) soundBox.Items.Add(choice);
        soundBox.SelectedIndex = RingSoundText.SoundIndex(Now().RingSound);
        AddRow("Ring sound", RingSoundText.SoundHelp, soundBox);

        var deviceBox = new ComboBox { Width = 280 };
        AutomationProperties.SetName(deviceBox, "Play it on");
        var deviceIds = new List<string?> { null };
        deviceBox.Items.Add(RingSoundText.HeadsetChoice);
        deviceBox.SelectedIndex = 0;
        AddRow("Play it on", RingSoundText.DeviceHelp, deviceBox);

        Slider volume = PageSlider("Volume", 0, 100, 5, Now().RingSoundVolume, v => Edit(h => h with { RingSoundVolume = (int)Math.Clamp(v, 0, 100) }),
            v => $"{(int)v}%", out TextBlock volumeText);
        AddRow("Volume", null, WithReadout(volume, volumeText));

        var play = new Button { Content = "Play", Style = Ui.Res<Style>("QuietButtonStyle") };
        AutomationProperties.SetName(play, "Play the ring sound");
        ToolTipService.SetToolTip(play, "Plays the ring sound once");
        AddRow("Try it", null, play);

        bool lockedOut = false;
        void Refresh()
        {
            bool on = soundBox.SelectedIndex > 0;
            deviceBox.IsEnabled = on;
            volume.IsEnabled = on;
            volumeText.Opacity = on ? 1 : 0.5;
            play.IsEnabled = on && !lockedOut;
        }

        soundBox.SelectionChanged += (_, _) =>
        {
            if (soundBox.SelectedIndex < 0) return;
            string? id = RingSoundText.SoundAt(soundBox.SelectedIndex);
            if (id != Now().RingSound) Edit(h => h with { RingSound = id });
            Refresh();
        };
        deviceBox.SelectionChanged += (_, _) =>
        {
            if (deviceBox.SelectedIndex < 0 || deviceBox.SelectedIndex >= deviceIds.Count) return;
            string? id = deviceIds[deviceBox.SelectedIndex];
            if (id != Now().RingSoundDevice) Edit(h => h with { RingSoundDevice = id });
        };
        play.Click += (_, _) =>
        {
            if (soundBox.SelectedIndex < 1) return;
            _config.Flush(); // the tray reads the device from the saved file
            AskTray(DeviceMessages.RingSoundPreviewName, soundBox.SelectedIndex, (nint)volume.Value + 1);
            lockedOut = true;
            Refresh();
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(RingSoundPlayLockoutSeconds);
            timer.IsRepeating = false;
            timer.Tick += (s, _) =>
            {
                s.Stop();
                lockedOut = false;
                Refresh();
            };
            timer.Start();
        };
        Refresh();
        _ = FillPlaybackDevicesAsync(deviceBox, deviceIds, Now().RingSoundDevice);
    }

    /// <summary>Lists the PC's playback devices after the headset choice; a saved device that is not connected stays selectable so the choice is not lost.</summary>
    private static async Task FillPlaybackDevicesAsync(ComboBox box, List<string?> ids, string? saved)
    {
        var found = new List<(string Name, string Id)>();
        try
        {
            var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
            foreach (DeviceInformation d in devices)
            {
                if (RingSoundText.ToCoreAudioId(d.Id) is { } id) found.Add((d.Name, id));
            }
        }
        catch (Exception)
        {
            // No list: the headset-or-default choice still works.
        }

        foreach ((string name, string id) in found)
        {
            ids.Add(id);
            box.Items.Add(name);
        }

        if (saved is not null)
        {
            int at = ids.IndexOf(saved);
            if (at < 0)
            {
                ids.Add(saved);
                box.Items.Add(RingSoundText.MissingDevice);
                at = ids.Count - 1;
            }

            box.SelectedIndex = at;
        }
    }
}
