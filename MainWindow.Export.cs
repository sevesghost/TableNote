using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace TableNote;

public partial class MainWindow
{
    private bool _imageBusy;

    // Image export and print are in the File menu (2.6.0: no toolbar buttons). _imageBusy keeps them single.

    /// <summary>
    /// What the open table shows. <see cref="ViewImageOptions.IsHidden"/> is the one hook for collapsed
    /// columns: it is <see cref="ViewImage.DefaultIsHidden"/> (width &lt;= 0) until column groups land,
    /// then assign ColumnGroupOps.IsHidden (same signature).
    /// </summary>
    private ViewImageOptions CaptureOptions()
    {
        var paints = ImagePartColors.From(_session.Doc);
        return new ViewImageOptions
        {
            VisibleRows = _visibleRows.ToList(),
            Now = DateTime.Now,
            Zoom = _settings.Scale / 100.0,
            IsHidden = ColumnGroupOps.IsHidden,
            RowHeight = SizeSpec.StoredRowHeight,
            CellColors = paints.CellColors,
            CellTextColors = paints.CellTextColors,
            HeaderColors = paints.HeaderColors,
            RowNumberColors = paints.RowNumberColors
        };
    }

    /// <summary>
    /// Clean picture of the visible rows: header, row numbers, grid, status and user colours, ⚠ and ⏰.
    /// Not the live editors, so selection, the caret cell, dropdown arrows, date buttons, and menus stay out.
    /// Widths and row heights come from the layout (no 420 cap, no fixed row height).
    /// IsHidden → ColumnGroupOps.IsHidden, CellColors → partColors, RowHeight → rowHeights, when those exist.
    /// </summary>
    private byte[] CaptureTable(ViewImageFormat format) =>
        ViewImage.Render(_session.Doc, format, CaptureOptions());

    private async Task ExportImageAsync()
    {
        if (_view != ViewMode.Edit || _imageBusy) return;
        CommitFocusedEdit();
        var options = CaptureOptions();
        var top = TopLevel.GetTopLevel(this);
        string? path = null;
        var format = ViewImageFormat.Png;
        if (top?.StorageProvider is { CanSave: true } sp)
        {
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Localizer.T("export.image_title"),
                SuggestedFileName = Localizer.T("export.default_name", DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)) + ".png",
                DefaultExtension = "png",
                ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    new FilePickerFileType(Localizer.T("export.png")) { Patterns = ["*.png"] },
                    new FilePickerFileType(Localizer.T("export.jpeg")) { Patterns = ["*.jpg", "*.jpeg"] }
                ]
            });
            if (file == null) return;
            path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path))
            {
                var pictureRun = ActivityLog.Shared.Begin(Localizer.T("activity.image", file.Name)); // 2.6.0
                try
                {
                    _imageBusy = true;
                    StatusText.Text = Localizer.T("export.image_running");
                    await LetBarDraw();
                    await using var stream = await file.OpenWriteAsync();
                    var bytes = CaptureTable(ViewImageFormat.Png);
                    await stream.WriteAsync(bytes);
                    StatusText.Text = Localizer.T("export.image_done", file.Name);
                    pictureRun.Done(StatusText.Text);
                    AppLog.Write("export", "image " + file.Name);
                }
                catch (Exception ex)
                {
                    StatusText.Text = Localizer.T("export.image_failed", ex.Message);
                    pictureRun.Fail(StatusText.Text);
                    AppLog.Write("export", "image failed: " + ex.Message);
                }
                finally
                {
                    _imageBusy = false;
                }
                return;
            }
            format = ViewImage.FormatForPath(path);
        }
        else
        {
            var dir = Path.GetDirectoryName(_path) ?? Path.GetTempPath();
            path = Path.Combine(dir, Localizer.T("export.default_name", DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)) + ".png");
        }

        _imageBusy = true;
        StatusText.Text = Localizer.T("export.image_running");
        var run = ActivityLog.Shared.Begin(Localizer.T("activity.image", Path.GetFileName(path))); // 2.6.0
        try
        {
            await LetBarDraw();
            File.WriteAllBytes(path, CaptureTable(format));
            StatusText.Text = Localizer.T("export.image_done", Path.GetFileName(path));
            run.Done(StatusText.Text);
            AppLog.Write("export", $"image {format} → {path}");
        }
        catch (Exception ex)
        {
            StatusText.Text = Localizer.T("export.image_failed", ex.Message);
            run.Fail(StatusText.Text);
            AppLog.Write("export", "image failed: " + ex.Message);
        }
        finally
        {
            _imageBusy = false;
        }
    }

    private async Task PrintViewAsync()
    {
        if (_view != ViewMode.Edit || _imageBusy) return;
        CommitFocusedEdit();
        _imageBusy = true;
        StatusText.Text = Localizer.T("print.running");
        var run = ActivityLog.Shared.Begin(Localizer.T("activity.printing")); // 2.6.0
        try
        {
            await LetBarDraw();
            ViewPrintFiles.CleanupExpired();
            var spec = new ViewPrintSpec();
            var pages = ViewImage.RenderPrintPages(_session.Doc, CaptureOptions(), spec);
            var paths = new List<string>(pages.Count);
            foreach (var page in pages)
                paths.Add(ViewPrintFiles.Write(page, ".png"));
            // Several pages go out as one PDF so the Windows print verb is a single job.
            // The preview still shows the PNGs. Closing the preview does not delete either file.
            string? job = null;
            if (pages.Count > 1)
            {
                try { job = ViewPrintFiles.Write(ViewPrintPdf.FromPngs(pages, spec.Dpi), ".pdf"); }
                catch (Exception ex) { AppLog.Write("export", "combined print file skipped: " + ex.Message); }
            }
            run.Done(Localizer.T("activity.printed", pages.Count));
            var dlg = new ViewPrintWindow(paths, job);
            await dlg.ShowDialog(this);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            StatusText.Text = Localizer.T("print.failed", ex.Message);
            run.Fail(StatusText.Text);
            AppLog.Write("export", "print failed: " + ex.Message);
        }
        finally
        {
            _imageBusy = false;
        }
    }
}

/// <summary>Print preview. Print sends one PDF when there are several pages, otherwise the page PNG. Closing does not delete those files.</summary>
public sealed class ViewPrintWindow : Window
{
    private readonly IReadOnlyList<string> _paths;
    private readonly string? _job;
    private readonly List<Bitmap> _bitmaps = new();
    private readonly TextBlock _status;

    public ViewPrintWindow(string pngPath) : this([pngPath]) { }

    public ViewPrintWindow(IReadOnlyList<string> pngPaths, string? jobPath = null)
    {
        _paths = pngPaths;
        _job = string.IsNullOrEmpty(jobPath) ? null : jobPath;
        Title = Localizer.T("print.preview_title");
        Width = 960;
        Height = 720;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = AppFonts.Thai;
        Background = new SolidColorBrush(Color.Parse("#E7E2D6"));
        var print = new Button
        {
            Content = Localizer.T("print.action"),
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 4)
        };
        var close = new Button { Content = Localizer.T("print.close"), Padding = new Thickness(12, 4), IsCancel = true };
        _status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.Parse("#3B3222"))
        };
        print.Click += (_, _) =>
        {
            var result = SendToPrinter();
            _status.Text = result.Kind switch
            {
                ViewPrintKind.Sent => Localizer.T("print.sent"),
                ViewPrintKind.Unavailable => Localizer.T("print.unavailable"),
                _ => Localizer.T("print.failed", result.Detail ?? "")
            };
        };
        close.Click += (_, _) => Close();
        var bar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FFF6C2")),
            Padding = new Thickness(12, 8),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = Localizer.T("print.preview_title"),
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 12, 0)
                    },
                    print, close, _status
                }
            }
        };
        DockPanel.SetDock(bar, Dock.Top);
        Closed += (_, _) => DisposeBitmaps();
        var pages = new StackPanel { Spacing = 16 };
        try
        {
            foreach (var page in _paths)
            {
                // Screen-sized copy for the preview. The file on disk stays at print resolution.
                var bitmap = LoadPreview(page);
                _bitmaps.Add(bitmap);
                pages.Children.Add(new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    MaxWidth = PreviewWidth,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
        }
        catch
        {
            DisposeBitmaps();
            throw;
        }
        Content = new DockPanel
        {
            Children =
            {
                bar,
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Padding = new Thickness(16),
                    Content = pages
                }
            }
        };
    }

    /// <summary>Preview bitmaps are decoded to this width so a long table does not keep every 200 dpi page in memory.</summary>
    public const int PreviewWidth = 880;

    private static Bitmap LoadPreview(string path)
    {
        using var stream = File.OpenRead(path);
        return Bitmap.DecodeToWidth(stream, PreviewWidth);
    }

    /// <summary>
    /// One shell print. A multi-page preview prints the combined PDF when that file exists.
    /// If that program has no print verb, the page images are sent instead (one dialog each).
    /// Nothing here deletes the temp files.
    /// </summary>
    private ViewPrintResult SendToPrinter()
    {
        if (_job != null)
        {
            var job = ViewImage.TryShellPrint(_job);
            if (job.Kind != ViewPrintKind.Failed) return job;
        }
        if (_paths.Count == 0) return new ViewPrintResult(ViewPrintKind.Failed, "missing file");
        ViewPrintResult result = new(ViewPrintKind.Unavailable, null);
        foreach (var page in _paths)
        {
            result = ViewImage.TryShellPrint(page);
            if (result.Kind != ViewPrintKind.Sent) break;
        }
        return result;
    }

    private void DisposeBitmaps()
    {
        foreach (var bitmap in _bitmaps)
        {
            try { bitmap.Dispose(); } catch { /* already disposed */ }
        }
        _bitmaps.Clear();
    }
}

/// <summary>
/// partColors entries the image and print use. <c>alert:</c> keys are skipped.
/// Row-number text colour and <c>section:table</c> are not drawn.
/// Header colour is <c>header:{columnId}</c> merged over <c>section:header</c>, same as on screen.
/// </summary>
public sealed class ImagePartColors
{
    public Dictionary<(int Row, int Column), string?> CellColors { get; } = new();
    public Dictionary<(int Row, int Column), string?> CellTextColors { get; } = new();
    public Dictionary<int, ViewHeaderColor?> HeaderColors { get; } = new();
    public Dictionary<int, string?> RowNumberColors { get; } = new();

    public static ImagePartColors From(NoteDocument doc)
    {
        var paints = new ImagePartColors();
        if (doc.PartColors == null || doc.PartColors.Count == 0) return paints;
        doc.EnsureRowIds();
        doc.EnsureColumnIds();
        PartColor? allHeaders = null;
        if (doc.PartColors.TryGetValue(PartColorKeys.Header, out var shared)) allHeaders = shared;
        foreach (var (key, color) in doc.PartColors)
        {
            if (string.IsNullOrEmpty(key) || key.StartsWith("alert:", StringComparison.Ordinal)) continue;
            if (key.StartsWith("cell:", StringComparison.Ordinal))
            {
                var body = key["cell:".Length..];
                var sep = body.IndexOf(':');
                if (sep <= 0 || sep >= body.Length - 1) continue;
                var row = doc.RowIds.IndexOf(body[..sep]);
                var col = doc.ColumnIds.IndexOf(body[(sep + 1)..]);
                if (row < 0 || col < 0 || color == null) continue;
                if (!string.IsNullOrWhiteSpace(color.Background)) paints.CellColors[(row, col)] = color.Background;
                if (!string.IsNullOrWhiteSpace(color.Foreground)) paints.CellTextColors[(row, col)] = color.Foreground;
            }
            else if (key.StartsWith("rownum:", StringComparison.Ordinal))
            {
                var row = doc.RowIds.IndexOf(key["rownum:".Length..]);
                if (row < 0 || string.IsNullOrWhiteSpace(color?.Background)) continue;
                paints.RowNumberColors[row] = color!.Background;
            }
        }
        for (var c = 0; c < doc.Columns.Count; c++)
        {
            doc.PartColors.TryGetValue(PartColorKeys.HeaderColumn(doc.ColumnIdAt(c)), out var own);
            var merged = PartColorOps.Merge(own, allHeaders);
            if (merged == null || (string.IsNullOrWhiteSpace(merged.Background) && string.IsNullOrWhiteSpace(merged.Foreground)))
                continue;
            paints.HeaderColors[c] = new ViewHeaderColor { Background = merged.Background, Text = merged.Foreground };
        }
        return paints;
    }
}
