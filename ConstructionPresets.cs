using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;   // System.Web.Extensions

namespace ModelDesk
{
    // Пресет = снимок выбранных таблиц конструкции в виде плоских массивов double
    // (row[0] = станция, row[1..] = поля по FieldNames). Robur-независим -> тестируется юнитом.
    internal sealed class PresetTable { public string Id { get; set; } public List<double[]> Rows { get; set; } }
    internal sealed class Preset { public string Name { get; set; } public List<PresetTable> Tables { get; set; } }

    // Чистая часть: сериализация + файловое хранилище пресетов. Save/Apply (Robur) — в
    // ConstructionPresetsRobur.cs; класс partial, чтобы тест-проект линковал только этот файл.
    internal static partial class ConstructionPresets
    {
        private static string Dir()
        {
            string d = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "Topomatic", "abr_construction_presets");
            Directory.CreateDirectory(d);
            return d;
        }

        internal static string ToJson(Preset p) => new JavaScriptSerializer().Serialize(p);
        internal static Preset FromJson(string s) => new JavaScriptSerializer().Deserialize<Preset>(s);

        internal static IReadOnlyList<string> List() =>
            Directory.Exists(Dir())
                ? Directory.GetFiles(Dir(), "*.json").Select(Path.GetFileNameWithoutExtension).ToList()
                : new List<string>();

        internal static Preset Load(string name) =>
            FromJson(File.ReadAllText(Path.Combine(Dir(), name + ".json")));
    }
}
