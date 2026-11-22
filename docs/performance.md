# Start-up speed and memory

2.6.1 makes TableNote open sooner and use less of the computer. This page records what was changed, why, and the
measurements behind it, so the settings in `TableNote.csproj` are not undone by accident.

## What changed in 2.6.1

| Change | Where | Why |
|---|---|---|
| ReadyToRun, one composite image | `TableNote.csproj` (win-x64): `PublishReadyToRun`, `PublishReadyToRunComposite` | .NET ships its libraries with precompiled machine code, but trimming rewrites them and drops it, so 2.6.0 compiled the whole program (.NET, Avalonia, TableNote) again at every start. The composite image puts precompiled code for all of it in the exe. |
| No compression inside the exe | `EnableCompressionInSingleFile=false` | A compressed bundle is unpacked into memory at every start. Uncompressed, the code is used straight from the file. The zip download is about the same size either way. |
| Tiered compilation off | `TieredCompilation=false` | With it on, .NET compiles the busiest methods a second time in the background during the first half minute (a quarter of a CPU core on the test machine). The precompiled code is good enough for a table program. |
| Loading screen waits at most 0.3 s | `SplashWindow.MinimumTime` | It waited 1.2 s even when the table was ready long before. |
| Lighter cells | `MainWindow.axaml` (`TnCellTextBox`), `MakeTextBox` | A cell was a standard text box: validation marks, a floating placeholder, left/right slots and two scroll bars, about 20 parts per cell. The cell template keeps the border (`PART_BorderElement`, which the selection colours paint), padding, text, caret and a scroller without bars: about 5 parts. |
| Menus made when they open | `BuildRowMenu`, `BuildColumnMenu` | Each row on screen carried its right-click menu (about 26 items) whether it was opened or not. |
| Start timeline in `tablenote.log` | `Core/StartClock.cs` | One line per start with the time of each step, counted from the moment Windows started the process. |

## Measurements

Same table (40 rows, 4 columns), median of 5 starts after one warm-up start. Test machine: Linux, 2 virtual cores of an
Intel Xeon at 2.1 GHz, software drawing (no graphics card), so the absolute numbers are slower than on a typical PC; the
differences between the builds are what matters. Times are milliseconds after the process started.

| Step | 2.6.0 | 2.6.1 |
|---|---:|---:|
| Program code starts | 181 | 67 |
| Interface ready | 940 | 291 |
| Loading screen drawn | 1,643 | 433 |
| Table built | 2,059 | 492 |
| Editor drawn (ready to use) | **3,704** | **993** |

| After the start | 2.6.0 | 2.6.1 |
|---|---:|---:|
| Private memory (not shared with other programs) | 148 MB | **91 MB** |
| Resident memory | 228 MB | 209 MB |
| CPU time used in the first 3 s | 5.2 s | **1.1 s** |
| CPU time used in the first 26 s | 6.0 s | **1.4 s** |
| CPU while idle (3 to 23 s after the start) | 3.9 % | 1.7 % |

Where the time went in 2.6.0 → 2.6.1, by setting (same machine, editor drawn):

| Build | Editor drawn | Private memory | Exe |
|---|---:|---:|---:|
| 2.6.0 (compressed, compiled at every start, 1.2 s loading screen) | 3,585 ms | 148 MB | 23 MB |
| + no compression | 3,524 ms | 138 MB | 39 MB |
| + ReadyToRun (composite) | 1,948 ms | 116 MB | 73 MB |
| ReadyToRun but compressed (rejected) | 2,038 ms | 161 MB | 37 MB |
| ReadyToRun per assembly, not composite (rejected) | 2,250 ms | 112 MB | 57 MB |
| + loading screen 0.3 s | 934 ms | 112 MB | 73 MB |
| + tiered compilation off | 938 ms | 106 MB | 73 MB |
| + lighter cells and menus (2.6.1) | 993 ms | 91 MB | 73 MB |

(Exe sizes here are the Linux test builds; the Windows exe of 2.6.1 is 69 MB, 29 MB zipped; 2.6.0 was 23 MB, 18 MB zipped.)

Rejected along the way:

- **Compressed ReadyToRun**: half the exe size, but the 25 MB of precompiled code is then unpacked into private memory at
  every start (more memory than 2.6.0) and the start is slower.
- **ReadyToRun per assembly** instead of one composite image: smaller, but about 0.3 s slower, because code cannot be
  shared or inlined across assemblies.
- **Tiered compilation on with PGO off**: about the same start, but about 75 % more CPU time in the first half minute.
- **A smaller first-generation GC budget** (`GCgen0size`): 10 MB less memory on this machine but a slower start, and it is
  an environment setting only, not a project setting.
- **Native AOT** (the whole program compiled to one native exe, no .NET compiler at all at run time): likely the fastest
  start and least memory, but .NET can only build it on Windows for Windows. It needs a Windows build machine (for
  example GitHub Actions) and a check of the Windows-only code (COM, wallpaper, tray) without the JIT.

## How to measure on your PC

Every start writes one line to `tablenote.log` (next to `note.json`, `%APPDATA%\TableNote` on Windows):

```
start timeline (ms after the process started): code 67 ms · ui ready 291 ms · splash drawn 433 ms · table built 492 ms · editor shown 702 ms · editor opened 948 ms · editor ready 992 ms · editor drawn 993 ms
```

- `code`: the exe has started the .NET runtime and TableNote's own code runs.
- `ui ready`: Avalonia, the fonts and the theme are loaded.
- `splash drawn`: the loading screen is on screen.
- `table built`: `note.json` is read and the editor window is built (not shown yet).
- `editor shown`: the editor is shown (at least 0.3 s after the loading screen appeared).
- `editor opened` / `editor ready`: the editor's first layout is done / its start-up work (tray icon, shortcut, reminders) is done.
- `editor drawn`: the first frame of the editor is on screen.

The first line of **History** in the activity bar gives the same total in seconds ("Opened the table: 40 rows (took 1.0 s)").

The first start after an update is slower: Windows (and an antivirus) reads the new exe for the first time, and the
graphics libraries are unpacked once into `%TEMP%\.net\TableNote`.

## Re-running the comparison

The numbers above came from Linux test builds of the same source:

```bash
dotnet publish TableNote.csproj -c Release -r linux-x64 --self-contained true -p:PublishTrimmed=true -p:TrimMode=partial \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false \
  -p:PublishReadyToRun=true -p:PublishReadyToRunComposite=true -p:TieredCompilation=false -o ./publish/linux-r2r
```

Start it a few times with `TABLENOTE_DATA` pointing to a copy of a `note.json`, read the `start timeline` line from the
`tablenote.log` beside it, and read the memory from `/proc/<pid>/smaps_rollup` (`Private_Dirty`) a few seconds after the
editor is drawn.
