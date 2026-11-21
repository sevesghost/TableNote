using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace TableNote;

/// <summary>
/// 2.6.0: View → System → Reminder sound…: mute, which sound (Windows' sounds or the user's own .wav), how loud
/// (0–100 %), and a test button. Above 50 % it asks "are you sure?" once the slider stops (and again on Save if it
/// was not answered); No puts the slider back to 50 %.
/// </summary>
public sealed class SoundDialog : Window
{
    public sealed record Result(string SoundId, int Volume, bool Muted);

    private static readonly IBrush LoudFg = new SolidColorBrush(Color.Parse("#B5530B"));
    private readonly ComboBox _sounds = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 220 };
    private readonly Slider _volume = new()
    {
        Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 10,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _volumeText = new() { MinWidth = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _loudNote = new() { Foreground = LoudFg, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _mute = new();
    private readonly DispatcherTimer _loudCheck = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private List<ReminderSounds.Choice> _choices;
    private readonly int _savedVolume;
    private bool _loudAccepted;
    private bool _asking;

    /// <summary>True while the "are you sure?" box is open (tests).</summary>
    public bool AskingLoud => _asking;

    public SoundDialog(string soundId, int volume, bool muted, double zoom = 1)
    {
        Title = Localizer.T("sound.title");
        Width = 440 * Math.Clamp(zoom, 0.8, 2);
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#FFF6C2"));
        FontFamily = AppFonts.Thai;
        _savedVolume = ReminderSounds.ClampVolume(volume);

        _mute.Content = Localizer.T("sound.mute");
        _mute.IsChecked = muted;
        _choices = ReminderSounds.Available();
        if (soundId != ReminderSounds.Default && _choices.All(c => c.Id != soundId))
            _choices.Add(new ReminderSounds.Choice(soundId, ReminderSounds.LabelFor(soundId)));
        _sounds.ItemsSource = _choices.Select(c => c.Label).ToList();
        _sounds.SelectedIndex = Math.Max(0, _choices.FindIndex(c => c.Id == soundId));

        var browse = new Button { Content = Localizer.T("sound.browse"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 4) };
        browse.Click += async (_, _) => await BrowseAsync();
        var soundRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(browse, 1);
        soundRow.Children.Add(_sounds);
        soundRow.Children.Add(browse);

        _volume.Value = _savedVolume;
        _volume.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Controls.Primitives.RangeBase.ValueProperty) return;
            ShowVolume();
            _loudCheck.Stop();
            if (VolumeValue >= ReminderSounds.LoudFrom && !_loudAccepted && VolumeValue != _savedVolume) _loudCheck.Start();
        };
        _loudCheck.Tick += async (_, _) => { _loudCheck.Stop(); await ConfirmLoudAsync(); };
        var volumeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_volumeText, 1);
        volumeRow.Children.Add(_volume);
        volumeRow.Children.Add(_volumeText);
        ShowVolume();

        var test = new Button { Content = Localizer.T("sound.test"), Padding = new Thickness(12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        test.Click += (_, _) => NotificationSound.Preview(SelectedId, VolumeValue);

        var save = new Button { Content = Localizer.T("common.save"), MinWidth = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0), HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = Localizer.T("common.cancel"), MinWidth = 90, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        save.Click += async (_, _) =>
        {
            if (VolumeValue >= ReminderSounds.LoudFrom && !_loudAccepted && VolumeValue != _savedVolume && !await ConfirmLoudAsync()) return;
            Close(new Result(SelectedId, VolumeValue, _mute.IsChecked == true));
        };
        cancel.Click += (_, _) => Close(null);

        var body = new StackPanel
        {
            Margin = new Thickness(18, 16),
            Spacing = 6,
            Children =
            {
                _mute,
                new TextBlock { Text = Localizer.T("sound.which"), Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeight.SemiBold },
                soundRow,
                new TextBlock { Text = Localizer.T("sound.volume"), Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeight.SemiBold },
                volumeRow,
                _loudNote,
                test,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0), Children = { save, cancel }
                }
            }
        };
        Content = Math.Abs(zoom - 1) < 0.001 ? body : new LayoutTransformControl { LayoutTransform = new ScaleTransform(zoom, zoom), Child = body };
        Closed += (_, _) => _loudCheck.Stop();
    }

    public int VolumeValue => ReminderSounds.ClampVolume((int)Math.Round(_volume.Value));
    public string SelectedId => _choices.ElementAtOrDefault(_sounds.SelectedIndex)?.Id ?? ReminderSounds.Default;

    /// <summary>Test seam: what the slider shows (as a user would drag it).</summary>
    public void SetVolume(int value) => _volume.Value = value;
    public void SetMuted(bool muted) => _mute.IsChecked = muted;

    private void ShowVolume()
    {
        var v = VolumeValue;
        _volumeText.Text = v + "%";
        var loud = v >= ReminderSounds.LoudFrom;
        _volumeText.Foreground = loud ? LoudFg : null;
        _loudNote.Text = v == 0 ? Localizer.T("sound.silent") : loud ? Localizer.T("sound.loud_note") : "";
        _loudNote.IsVisible = _loudNote.Text.Length > 0;
    }

    /// <summary>"Louder than half — are you sure?" Yes keeps it (for this dialog); No puts the slider back to 50 %.</summary>
    private async Task<bool> ConfirmLoudAsync()
    {
        if (_asking || _loudAccepted || VolumeValue < ReminderSounds.LoudFrom) return true;
        _asking = true;
        try
        {
            var yes = await MessageBox.Show(this, Localizer.T("sound.loud_confirm", VolumeValue), Localizer.T("sound.title"),
                Localizer.T("sound.loud_yes"), Localizer.T("sound.loud_no"));
            if (yes) _loudAccepted = true;
            else _volume.Value = ReminderSounds.DefaultVolume;
            return yes;
        }
        finally { _asking = false; }
    }

    private async Task BrowseAsync()
    {
        if (StorageProvider is not { CanOpen: true } sp) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localizer.T("sound.browse_title"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Localizer.T("sound.wav_files")) { Patterns = ["*.wav"] }]
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        _choices.RemoveAll(c => ReminderSounds.IsOwnFile(c.Id));
        _choices.Add(new ReminderSounds.Choice(path, ReminderSounds.LabelFor(path)));
        _sounds.ItemsSource = _choices.Select(c => c.Label).ToList();
        _sounds.SelectedIndex = _choices.Count - 1;
    }
}
