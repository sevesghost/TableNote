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

    private void InitImageExport()
    {
        ExportImageButton.Click += async (_, _) => await ExportImageAsync();
        PrintButton.Click += async (_, _) => await PrintViewAsync();
    }

    private void ApplyImageExportTexts()
    {
        ExportImageButton.Content = Localizer.T("toolbar.export_image");
        ToolTip.SetTip(ExportImageButton, Localizer.T("tip.export_image"));
        PrintButton.Content = Localizer.T("toolbar.print");
        ToolTip.SetTip(PrintButton, Localizer.T("tip.print"));
    }

    /// <summary>
    /// What the open table shows. <see cref="ViewImageOptions.IsHidden"/> is the one hook for collapsed
    /// columns: it is <see cref="ViewImage.DefaultIsHidden"/> (width &lt;= 0) until column groups land,
    /// then assign ColumnGroupOps.IsHidden (same signature).
    /// </summary>
    private ViewImageOptions CaptureOptions() => new()
    {
        VisibleRows = _visibleRows.ToList(),
        Now = DateTime.Now,
        Zoom = _settings.Scale / 100.0,
        IsHidden = ViewImage.DefaultIsHidden
    };

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
                try
                {
                    _imageBusy = true;
                    ExportImageButton.IsEnabled = false;
                    StatusText.Text = Localizer.T("export.image_running");
                    await using var stream = await file.OpenWriteAsync();
                    var bytes = CaptureTable(ViewImageFormat.Png);
                    await stream.WriteAsync(bytes);
                    StatusText.Text = Localizer.T("export.image_done", file.Name);
                    AppLog.Write("export", "image " + file.Name);
                }
                catch (Exception ex)
                {
                    StatusText.Text = Localizer.T("export.image_failed", ex.Message);
                    AppLog.Write("export", "image failed: " + ex.Message);
                }
                finally
                {
                    _imageBusy = false;
                    ExportImageButton.IsEnabled = true;
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
        ExportImageButton.IsEnabled = false;
        StatusText.Text = Localizer.T("export.image_running");
        try
        {
            File.WriteAllBytes(path, CaptureTable(format));
            StatusText.Text = Localizer.T("export.image_done", Path.GetFileName(path));
            AppLog.Write("export", $"image {format} → {path}");
        }
        catch (Exception ex)
        {
            StatusText.Text = Localizer.T("export.image_failed", ex.Message);
            AppLog.Write("export", "image failed: " + ex.Message);
        }
        finally
        {
            _imageBusy = false;
            ExportImageButton.IsEnabled = true;
        }
    }

    private async Task PrintViewAsync()
    {
        if (_view != ViewMode.Edit || _imageBusy) return;
        CommitFocusedEdit();
        _imageBusy = true;
        PrintButton.IsEnabled = false;
        StatusText.Text = Localizer.T("print.running");
        try
        {
            var pages = ViewImage.RenderPrintPages(_session.Doc, CaptureOptions());
            var paths = new List<string>(pages.Count);
            foreach (var page in pages)
            {
                var pagePath = Path.Combine(Path.GetTempPath(), "TableNote-print-" + Guid.NewGuid().ToString("N") + ".png");
                await File.WriteAllBytesAsync(pagePath, page);
                paths.Add(pagePath);
            }
            var dlg = new ViewPrintWindow(paths);
            await dlg.ShowDialog(this);
            StatusText.Text = Localizer.T("print.preview_title");
            foreach (var pagePath in paths) { try { File.Delete(pagePath); } catch { /* closed */ } }
        }
        catch (Exception ex)
        {
            StatusText.Text = Localizer.T("print.failed", ex.Message);
            AppLog.Write("export", "print failed: " + ex.Message);
        }
        finally
        {
            _imageBusy = false;
            PrintButton.IsEnabled = true;
        }
    }
}

/// <summary>Print preview. The page image has no toolbar; Print uses the Windows shell verb on that PNG.</summary>
public sealed class ViewPrintWindow : Window
{
    private readonly IReadOnlyList<string> _paths;
    private readonly TextBlock _status;

    public ViewPrintWindow(string pngPath) : this([pngPath]) { }

    public ViewPrintWindow(IReadOnlyList<string> pngPaths)
    {
        _paths = pngPaths;
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
            ViewPrintResult result = new(ViewPrintKind.Unavailable, null);
            foreach (var page in _paths)
            {
                result = ViewImage.TryShellPrint(page);
                if (result.Kind != ViewPrintKind.Sent) break;
            }
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
        var pages = new StackPanel { Spacing = 16 };
        foreach (var page in _paths)
            pages.Children.Add(new Image
            {
                Source = new Bitmap(page),
                Stretch = Stretch.None,
                HorizontalAlignment = HorizontalAlignment.Center
            });
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
}
