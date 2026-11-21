namespace TableNote;

/// <summary>2.6.0: reminder sound settings (View → System → Reminder sound…), kept in settings.json.</summary>
public partial class MainWindow
{
    private void ApplySoundSettings()
    {
        NotificationSound.SoundId = _settings.ReminderSound;
        NotificationSound.Volume = _settings.ReminderVolume;
    }

    private async Task OpenSoundSettingsAsync()
    {
        if (_view != ViewMode.Edit) return;
        var dialog = new SoundDialog(_settings.ReminderSound, _settings.ReminderVolume, _session.Doc.MuteSound, _settings.Scale / 100.0);
        var result = await dialog.ShowDialog<SoundDialog.Result?>(this);
        if (result == null) return;
        _settings.ReminderSound = result.SoundId;
        _settings.ReminderVolume = result.Volume;
        _settings.Save(_path);
        ApplySoundSettings();
        if ((MuteSoundBox.IsChecked == true) != result.Muted) MuteSoundBox.IsChecked = result.Muted; // saves note.json, sets Muted
        StatusText.Text = result.Muted
            ? Localizer.T("ui.sound_off")
            : Localizer.T("sound.saved", ReminderSounds.LabelFor(result.SoundId), result.Volume);
        AppLog.Write("sound", $"reminder sound {(result.SoundId.Length == 0 ? "Windows warning" : Path.GetFileName(result.SoundId))}, " +
                              $"volume {result.Volume} %, muted {result.Muted}");
    }
}
