# Contributing to TableNote

Thanks for helping! Bug reports, fixes, and new translations are all welcome.

## Reporting a bug

Open an issue with:

- the TableNote version (Help window, or the title of the change log),
- your Windows version and display language,
- the steps to reproduce it,
- the relevant lines of `tablenote.log` (next to `note.json`, see [Data storage](README.md#data-storage)). Remove any task text you don't want to share.

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build -c Release
dotnet run -c Release --project tests/PersistenceTests
dotnet run -c Release --project tests/LangTests
```

The Windows build is `dotnet publish -c Release -r win-x64` (see [Building from source](README.md#building-from-source)). The app also runs on Linux for UI testing, but background mode and Windows notifications only work on Windows.

## Pull requests

- Keep each pull request to one change, and say what you tested.
- Every user-visible text goes through the language files (below). Don't put English or Thai text directly in C# or XAML.
- Don't change the `note.json` format without a version bump and a migration; existing files must keep opening without data loss.
- All tests must pass.

## Adding or improving a language

All interface text lives in JSON files under `lang/`, one per language, embedded in the app at build time: `lang/en.json` (English, the default and the fallback) and `lang/th.json` (Thai). Each file is a flat JSON object that maps a `group.name` key to its text (comments and trailing commas are allowed). In code, a text is read with `Localizer.T("export.done", count, path)`, where `{0}`, `{1}`… are the arguments in order:

```json
{
  "_language.name": "English",
  "task_status.todo": "Not started",
  "export.done": "Exported {0} rows to {1}"
}
```

To add a language:

1. Copy `lang/en.json` to `lang/<code>.json`, where `<code>` is a lowercase language code (for example `ja.json` or `pt-br.json`).
2. Set `_language.name` to the language's own name (for example `日本語`). This is what the language menu shows.
3. Translate every value. Don't change the keys, and keep each `{0}`, `{1}`… placeholder.
4. Build. The language button and the tray language menu pick up the new file on their own; no code change is needed.
5. Run `tests/LangTests`. It fails if a key is missing or empty in any language, if the `{0}` placeholders differ from English, or if the code uses a key that doesn't exist.

When you add or change a text in the code, add the key to **every** file in `lang/`, not just English, or `LangTests` will fail. A key that is missing from a language is shown in English; if English lacks it too, the key itself is shown. You don't need to edit `TableNote.csproj`: every `lang/*.json` is embedded automatically.

Not translated through `lang/` (yet):

- The Help window and change log text, in `Core/AppInfo.cs` (English and Thai only; other languages show English).
- Words TableNote reads from your data, such as the Thai status words `เสร็จแล้ว` and Buddhist Era years. These must stay, so that existing `note.json` files keep working.
