using System.Text.Json.Serialization;

namespace TableNote;

/// <summary>
/// Source-generated JSON metadata for every type TableNote writes or reads with <c>JsonSerializer</c>
/// (note.json in <see cref="NoteStore"/>, undo snapshots in <see cref="Snapshot"/>, wallpaper.json in
/// <see cref="WallpaperRecordStore"/>, sketch.json in <see cref="SketchStore"/>). The Windows exe is trimmed,
/// and a trimmed app cannot use reflection-based <c>JsonSerializer</c>; TableNote.csproj switches reflection off in
/// every build so a missed call fails in tests too. Each caller binds its own options (naming, indenting, encoder)
/// with <c>new TableNoteJson(options)</c>, so the JSON text is unchanged. A new serialized type must be listed here.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NoteStore.FileDto))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, ReminderInfo>))]
[JsonSerializable(typeof(Dictionary<string, ColumnSetting>))]
[JsonSerializable(typeof(Dictionary<string, RowStyle>))]
[JsonSerializable(typeof(Dictionary<string, List<RowEvent>>))]
[JsonSerializable(typeof(Snapshot.Dto))]
[JsonSerializable(typeof(WallpaperRecord))]
[JsonSerializable(typeof(SketchStore.SketchFileDto))]
internal sealed partial class TableNoteJson : JsonSerializerContext;
