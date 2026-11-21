using Avalonia;
using Avalonia.Headless;
using TableNote;

// 2.6.0 days counter column and row history.
var fail = 0;
void Ok(bool cond, string name, string detail)
{
    Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond || detail.Length == 0 ? "" : " — " + detail));
    if (!cond) fail++;
}
AppLog.PathOverride = Path.Combine(Path.GetTempPath(), "tn-days-tests.log");
Avalonia.AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(AppFonts.ManagerOptions) // as Program.BuildAvaloniaApp
    .SetupWithoutStarting();
CoreTests.Run(Ok);
UiTests.Run(Ok);
Console.WriteLine(fail == 0 ? "DaysTests: all passed" : $"DaysTests: {fail} failed");
return fail;
