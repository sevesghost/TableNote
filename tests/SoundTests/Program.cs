using System.Buffers.Binary;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TableNote;

// 2.6.0 reminder sound settings.
var fail = 0;
void Ok(bool cond, string name, string detail = "")
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond || detail.Length == 0 ? "" : " — " + detail));
    if (!cond) fail++;
}
AppLog.PathOverride = Path.Combine(Path.GetTempPath(), "tn-sound-tests.log");
Localizer.SetLanguage("th");

// ── volume scaling ──
byte[] Wave(int format, int bits, byte[] data, bool extensible = false)
{
    var fmtLen = extensible ? 40 : 16;
    var ms = new MemoryStream();
    var w = new BinaryWriter(ms);
    w.Write("RIFF"u8); w.Write(4 + 8 + fmtLen + 8 + data.Length); w.Write("WAVE"u8);
    w.Write("LIST"u8); w.Write(4); w.Write("INFO"u8); // a chunk before fmt
    w.Write("fmt "u8); w.Write(fmtLen);
    w.Write((ushort)(extensible ? 0xFFFE : format)); w.Write((ushort)1); w.Write(8000); w.Write(8000 * bits / 8); w.Write((ushort)(bits / 8)); w.Write((ushort)bits);
    if (extensible) { w.Write((ushort)22); w.Write((ushort)bits); w.Write(0); w.Write((ushort)format); w.Write(new byte[14]); }
    w.Write("data"u8); w.Write(data.Length); w.Write(data);
    return ms.ToArray();
}
int DataAt(byte[] wav) { for (var i = 12; i < wav.Length - 4; i++) if (wav.AsSpan(i, 4).SequenceEqual("data"u8)) return i + 8; return -1; }

var pcm16 = new byte[8];
BinaryPrimitives.WriteInt16LittleEndian(pcm16.AsSpan(0), 1000);
BinaryPrimitives.WriteInt16LittleEndian(pcm16.AsSpan(2), -1000);
BinaryPrimitives.WriteInt16LittleEndian(pcm16.AsSpan(4), short.MaxValue);
BinaryPrimitives.WriteInt16LittleEndian(pcm16.AsSpan(6), short.MinValue);
var w16 = Wave(1, 16, pcm16);
var half = ReminderSounds.Scaled(w16, 50)!;
var d = DataAt(half);
Ok(BinaryPrimitives.ReadInt16LittleEndian(half.AsSpan(d)) == 500 && BinaryPrimitives.ReadInt16LittleEndian(half.AsSpan(d + 2)) == -500 &&
   BinaryPrimitives.ReadInt16LittleEndian(half.AsSpan(d + 4)) == 16384, "16-bit samples are halved at 50 %");
Ok(ReminderSounds.Scaled(w16, 100)!.SequenceEqual(w16), "100 % leaves the sound as it is");
Ok(ReminderSounds.Scaled(w16, 0)!.AsSpan(d, 8).ToArray().All(b => b == 0), "0 % is silence");
Ok(BinaryPrimitives.ReadInt16LittleEndian(w16.AsSpan(d)) == 1000, "the original file bytes are not changed");
var w8 = ReminderSounds.Scaled(Wave(1, 8, [228, 28, 128]), 50)!;
Ok(w8[DataAt(w8)] == 178 && w8[DataAt(w8) + 1] == 78 && w8[DataAt(w8) + 2] == 128, "8-bit samples scale around 128");
var w24 = ReminderSounds.Scaled(Wave(1, 24, [0x00, 0x10, 0x00, 0x00, 0xF0, 0xFF]), 50)!;
var at24 = DataAt(w24);
Ok((w24[at24] | (w24[at24 + 1] << 8) | ((sbyte)w24[at24 + 2] << 16)) == 2048 &&
   (w24[at24 + 3] | (w24[at24 + 4] << 8) | ((sbyte)w24[at24 + 5] << 16)) == -2048, "24-bit samples, sign kept");
var f = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(f, 0.8f);
var wf = ReminderSounds.Scaled(Wave(3, 32, f), 25)!;
Ok(Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(wf.AsSpan(DataAt(wf))) - 0.2f) < 1e-6, "32-bit float samples");
Ok(ReminderSounds.Scaled(Wave(1, 16, pcm16, extensible: true), 50) is { } ext && BinaryPrimitives.ReadInt16LittleEndian(ext.AsSpan(DataAt(ext))) == 500,
    "WAVE_FORMAT_EXTENSIBLE PCM too");
Ok(ReminderSounds.Scaled(Wave(2, 4, [1, 2, 3, 4]), 50) == null, "ADPCM and other packed formats are played as they are");
Ok(ReminderSounds.Scaled("not a wave at all, just some text"u8.ToArray(), 50) == null, "not a .wav: null");

// ── choices ──
var media = Directory.CreateTempSubdirectory("tn-media").FullName;
File.WriteAllBytes(Path.Combine(media, "Alarm03.wav"), w16);
File.WriteAllBytes(Path.Combine(media, "chimes.wav"), w16);
File.WriteAllBytes(Path.Combine(media, "Windows Exclamation.wav"), w16);
ReminderSounds.MediaFolderOverride = media;
var choices = ReminderSounds.Available();
Ok(choices.Select(c => c.Label).SequenceEqual(["เสียงเตือนของ Windows (แบบเดิม)", "กระดิ่ง", "นาฬิกาปลุก 3"]),
    "the list: the Windows warning first, then the Windows sounds that exist", string.Join(" | ", choices.Select(c => c.Label)));
Ok(ReminderSounds.Resolve("chimes.wav") == Path.Combine(media, "chimes.wav") && ReminderSounds.Resolve("Ring05.wav") == null,
    "a Windows sound resolves to its file; a missing one to nothing");
Ok(ReminderSounds.Resolve("", () => null) == Path.Combine(media, "Windows Exclamation.wav"),
    "the default is the user's Windows warning sound, else Windows Exclamation.wav");
var own = Path.Combine(media, "my bell.wav");
File.WriteAllBytes(own, w16);
Ok(ReminderSounds.Resolve(own) == own && ReminderSounds.LabelFor(own) == "ไฟล์ของฉัน: my bell.wav", "an own .wav by its path");

// ── settings.json ──
var dir = Directory.CreateTempSubdirectory("tn-sound").FullName;
var note = Path.Combine(dir, "note.json");
var settings = AppSettings.Load(note);
Ok(settings.ReminderSound == "" && settings.ReminderVolume == 50, "defaults: the Windows warning sound at 50 %");
settings.Save(note);
Ok(!File.ReadAllText(AppSettings.PathFor(note)).Contains("reminder"), "defaults are not written");
settings.ReminderSound = @"C:\Users\me\เสียง ""พิเศษ""\bell.wav";
settings.ReminderVolume = 70;
settings.Save(note);
var back = AppSettings.Load(note);
Ok(back.ReminderSound == settings.ReminderSound && back.ReminderVolume == 70, "sound path (quotes, backslashes, Thai) and volume survive", back.ReminderSound);
File.WriteAllText(AppSettings.PathFor(note), "{ \"language\": \"th\", \"reminderVolume\": 250 }");
Ok(AppSettings.Load(note).ReminderVolume == 100, "a volume over 100 reads as 100");

// ── the dialog ──
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(AppFonts.ManagerOptions)
    .SetupWithoutStarting();
var owner = new Window { Width = 600, Height = 400 };
owner.Show();
var dlg = new SoundDialog("chimes.wav", 50, muted: false);
var shown = dlg.ShowDialog<SoundDialog.Result?>(owner);
Pump(100);
Ok(dlg.SelectedId == "chimes.wav" && dlg.VolumeValue == 50, "the dialog opens with the saved sound and volume");
var previews = new List<(string, int)>();
NotificationSound.PreviewOverride = (id, v) => previews.Add((id, v));
Button(dlg, "▶ ทดลองเสียง").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
Ok(previews.SequenceEqual([("chimes.wav", 50)]), "Test plays the chosen sound at the chosen volume");

dlg.SetVolume(80);
SliderStops(dlg);
var ask = dlg.OwnedWindows.FirstOrDefault(w => w.IsVisible);
Ok(ask != null && dlg.AskingLoud, "above 50 % it asks whether you are sure");
if (ask != null)
{
    Ok(Texts(ask).Any(t => t.Contains("80%", StringComparison.Ordinal)), "the question says how loud", string.Join(" | ", Texts(ask)));
    Button(ask, "ไม่ กลับไป 50%").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    Pump(100);
}
Ok(dlg.VolumeValue == 50 && !dlg.AskingLoud, "No puts the volume back to 50 %", dlg.VolumeValue.ToString());
dlg.SetVolume(70);
SliderStops(dlg);
ask = dlg.OwnedWindows.FirstOrDefault(w => w.IsVisible);
if (ask != null) { Button(ask, "แน่ใจ ใช้ระดับนี้").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Pump(100); }
Ok(dlg.VolumeValue == 70, "Yes keeps it", dlg.VolumeValue.ToString());
dlg.SetVolume(90);
Ok(!LoudTimer(dlg).IsEnabled, "asked once per dialog (no new question after Yes)");
dlg.SetMuted(true);
Button(dlg, "บันทึก").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
Pump(100);
Ok(shown.IsCompleted && shown.Result == new SoundDialog.Result("chimes.wav", 90, true), "Save returns sound, volume and mute",
    shown.IsCompleted ? shown.Result?.ToString() ?? "null" : "open");

var dlg2 = new SoundDialog("", 50, false);
var shown2 = dlg2.ShowDialog<SoundDialog.Result?>(owner);
Pump(80);
dlg2.SetVolume(65);
Button(dlg2, "บันทึก").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); // before the slider question
Pump(150);
var ask2 = dlg2.OwnedWindows.FirstOrDefault(w => w.IsVisible);
Ok(ask2 != null && !shown2.IsCompleted, "Save on a loud volume asks first");
if (ask2 != null) { Button(ask2, "แน่ใจ ใช้ระดับนี้").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Pump(150); }
Ok(shown2.IsCompleted && shown2.Result?.Volume == 65, "then saves", shown2.IsCompleted ? shown2.Result?.ToString() ?? "null" : "open");

var dlg3 = new SoundDialog("", 80, false);
var shown3 = dlg3.ShowDialog<SoundDialog.Result?>(owner);
Pump(80);
Button(dlg3, "บันทึก").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
Pump(150);
Ok(shown3.IsCompleted && shown3.Result?.Volume == 80, "a loud volume chosen before is not asked about again");

Console.WriteLine(fail == 0 ? "SoundTests: all passed" : $"SoundTests: {fail} failed");
return fail;

// The headless dispatcher does not run this kind of timer by itself: check it was started, then do what its tick does.
static DispatcherTimer LoudTimer(SoundDialog d) =>
    (DispatcherTimer)typeof(SoundDialog).GetField("_loudCheck", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(d)!;
void SliderStops(SoundDialog d)
{
    Ok(LoudTimer(d).IsEnabled, $"moving the slider to {d.VolumeValue} % starts the question timer");
    LoudTimer(d).Stop();
    _ = (Task<bool>)typeof(SoundDialog).GetMethod("ConfirmLoudAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(d, null)!;
    Pump(150);
}

static Button Button(Window w, string text) =>
    w.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == text);
static List<string> Texts(Window w) => w.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
static void Pump(int ms)
{
    var sw = Stopwatch.StartNew();
    do { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(1); Thread.Sleep(10); } while (sw.ElapsedMilliseconds < ms);
}
