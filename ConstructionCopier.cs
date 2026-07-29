using System;
using System.Collections.Generic;
using System.Linq;
using Topomatic.Alg.Road;
using Topomatic.Alg.Road.Urb;

namespace ModelDesk
{
    // Итог операции копирования: сколько дорог/таблиц/строк записано + ошибки по целям.
    internal sealed class CopyResult
    {
        public int Roads, Tables, Rows;
        public List<string> Errors = new List<string>();
    }

    // Копирование выбранных таблиц верха конструкции с дороги-источника на дороги-приёмники
    // с пересчётом станций (StationMapper), транзакционно (BeginUpdate/EndUpdate -> Undo).
    // Строки — value-type struct: запись = Clear + пересборка IList клонами с новой станцией.
    internal static class ConstructionCopier
    {
        // Диапазон станций таблицы (min/max по строкам); пустая -> (0,0).
        private static (double min, double max) Range(ConstructionTable t)
        {
            double mn = double.MaxValue, mx = double.MinValue;
            foreach (var o in t.Rows) { double s = t.GetStation(o); if (s < mn) mn = s; if (s > mx) mx = s; }
            if (t.Rows.Count == 0) { mn = 0; mx = 0; }
            return (mn, mx);
        }

        internal static CopyResult Copy(RoadAlignment src, IReadOnlyList<RoadAlignment> targets,
            ISet<string> tableIds, MappingMode mode, double offset)
        {
            var res = new CopyResult();
            var srcTables = ConstructionTables.Read(src).Where(t => tableIds.Contains(t.Id)).ToList();

            foreach (var tgt in targets)
            {
                UrbParams urb = tgt?.Urb;
                if (urb == null) continue;
                var tgtTables = ConstructionTables.Read(tgt).ToDictionary(t => t.Id);
                try
                {
                    urb.BeginUpdate("Копирование конструкции");
                    foreach (var st in srcTables)
                    {
                        if (!tgtTables.TryGetValue(st.Id, out var tt)) continue;
                        var (smin, smax) = Range(st);
                        var (tmin, tmax) = Range(tt);           // приёмник может быть пуст -> (0,0)
                        // Пустой приёмник не имеет собственного диапазона -> вырождается в диапазон
                        // источника (для Absolute несущественно; для Relative/Offset даёт копию 1:1).
                        var map = StationMapper.Build(mode, smin, smax,
                                                      tmin == tmax ? smin : tmin,
                                                      tmax == tmin ? smax : tmax, offset);
                        var newRows = new List<object>(st.Rows.Count);
                        foreach (var o in st.Rows) newRows.Add(st.WithStation(o, map(st.GetStation(o))));
                        tt.Rows.Clear();
                        foreach (var r in newRows) tt.Rows.Add(r);
                        res.Tables++; res.Rows += newRows.Count;
                    }
                    urb.EndUpdate();
                    res.Roads++;
                }
                catch (Exception ex)
                {
                    try { urb.EndUpdate(); } catch { }
                    res.Errors.Add((tgt != null ? "дорога" : "?") + ": " + ex.Message);
                }
            }
            return res;
        }
    }
}
