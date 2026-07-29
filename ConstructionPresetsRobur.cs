using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Topomatic.Alg.Road;
using Topomatic.Alg.Road.Urb;

namespace ModelDesk
{
    // Robur-часть пресетов: чтение таблиц дороги в пресет (Save) и запись пресета на дороги
    // (Apply) с пересчётом станций. Не линкуется в тест-проект (тянет Robur).
    internal static partial class ConstructionPresets
    {
        internal static void Save(string name, RoadAlignment road, ISet<string> tableIds)
        {
            var tables = ConstructionTables.Read(road).Where(t => tableIds.Contains(t.Id));
            var p = new Preset { Name = name, Tables = new List<PresetTable>() };
            foreach (var t in tables)
            {
                var pt = new PresetTable { Id = t.Id, Rows = new List<double[]>() };
                foreach (var o in t.Rows)
                {
                    var row = new double[1 + t.FieldNames.Length];
                    row[0] = t.GetStation(o);
                    for (int i = 0; i < t.FieldNames.Length; i++) row[i + 1] = t.GetField(o, i);
                    pt.Rows.Add(row);
                }
                p.Tables.Add(pt);
            }
            File.WriteAllText(Path.Combine(Dir(), name + ".json"), ToJson(p));
        }

        // Заполняет таблицы приёмников из строк пресета. Приёмник не имеет собственного
        // диапазона -> вырождается в диапазон источника пресета (для Relative/Offset — 1:1).
        // Struct-строка реконструируется через default(T) + WithStation/WithField.
        internal static CopyResult Apply(Preset preset, IReadOnlyList<RoadAlignment> targets,
            MappingMode mode, double offset)
        {
            var res = new CopyResult();
            foreach (var tgt in targets)
            {
                UrbParams urb = tgt?.Urb;
                if (urb == null) continue;
                var tt = ConstructionTables.Read(tgt).ToDictionary(t => t.Id);
                try
                {
                    urb.BeginUpdate("Применение пресета конструкции");
                    foreach (var pt in preset.Tables)
                    {
                        if (!tt.TryGetValue(pt.Id, out var t)) continue;
                        double smin = pt.Rows.Count > 0 ? pt.Rows.Min(r => r[0]) : 0;
                        double smax = pt.Rows.Count > 0 ? pt.Rows.Max(r => r[0]) : 0;
                        var map = StationMapper.Build(mode, smin, smax, smin, smax, offset);
                        Type rowType = t.Rows.GetType().GetGenericArguments()[0];
                        t.Rows.Clear();
                        foreach (var r in pt.Rows)
                        {
                            object row = Activator.CreateInstance(rowType);
                            row = t.WithStation(row, map(r[0]));
                            for (int i = 0; i < t.FieldNames.Length && i + 1 < r.Length; i++)
                                row = t.WithField(row, i, r[i + 1]);
                            t.Rows.Add(row);
                        }
                        res.Tables++; res.Rows += pt.Rows.Count;
                    }
                    urb.EndUpdate();
                    res.Roads++;
                }
                catch (Exception ex)
                {
                    try { urb.EndUpdate(); } catch { }
                    res.Errors.Add(ex.Message);
                }
            }
            return res;
        }
    }
}
