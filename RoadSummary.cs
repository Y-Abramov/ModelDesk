using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Topomatic.Alg.Road;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;

namespace ModelDesk
{
    // Сводка по дороге для вкладки «Сводка». Все поля — строки; недоступное -> «—».
    internal sealed class RoadSummaryData
    {
        public string Name = "—", Length = "—", Stationing = "—", Lanes = "—",
                      WidthRange = "—", Layers = "—", Sections = "—", Surfaces = "—";
    }

    // Собирает макс-сводку по дороге + определяет диапазон станций.
    // Геометрия (длина/ВУ) — best-effort рефлексией Alignment; недоступное -> «—», без исключений.
    internal static class RoadSummary
    {
        // Диапазон станций: из Alignment-геттеров (StartStation/EndStation) при наличии,
        // иначе min/max станций таблиц конструкции.
        internal static (double min, double max) StationRange(RoadAlignment road)
        {
            double? start = TryDouble(road, "StartStation");
            double? end = TryDouble(road, "EndStation") ?? TryDouble(road, "FinishStation")
                          ?? TryDouble(road, "MaxChangeStation");
            if (start.HasValue && end.HasValue && end.Value >= start.Value)
                return (start.Value, end.Value);

            double mn = double.MaxValue, mx = double.MinValue;
            foreach (var t in ConstructionTables.Read(road))
                foreach (var o in t.Rows)
                { double s = t.GetStation(o); if (s < mn) mn = s; if (s > mx) mx = s; }
            if (mn == double.MaxValue) { mn = 0; mx = 0; }
            return (mn, mx);
        }

        internal static RoadSummaryData Read(RoadAlignment road, IProjectModel model)
        {
            var d = new RoadSummaryData();
            if (road == null) return d;

            try
            {
                string fn = PluginCoreOps.GetFileName(model);
                if (!string.IsNullOrEmpty(fn)) d.Name = System.IO.Path.GetFileNameWithoutExtension(fn);
            }
            catch { }

            var (mn, mx) = StationRange(road);
            d.Stationing = Pk(mn) + " – " + Pk(mx);

            double? length = TryDouble(road, "Length");
            if (!length.HasValue && mx >= mn) length = mx - mn;
            if (length.HasValue) d.Length = length.Value.ToString("F1") + " м";

            var tables = ConstructionTables.Read(road);

            var w = tables.FirstOrDefault(t => t.Id == "widths");
            if (w != null && w.Rows.Count > 0)
            {
                var allW = new List<double>();
                int maxLanes = 0;
                foreach (var o in w.Rows)
                {
                    int lanes = 0;
                    for (int i = 0; i < w.FieldNames.Length; i++)
                    {
                        double v = w.GetField(o, i);
                        if (v > 0) { allW.Add(v); lanes++; }
                    }
                    if (lanes > maxLanes) maxLanes = lanes;
                }
                if (allW.Count > 0)
                    d.WidthRange = allW.Min().ToString("F2") + " – " + allW.Max().ToString("F2") + " м";
                if (maxLanes > 0) d.Lanes = maxLanes.ToString();
            }

            var con = tables.FirstOrDefault(t => t.Id == "construction");
            if (con != null) d.Layers = con.Rows.Count.ToString() + " строк";

            try { d.Sections = RoadModelAccess.GetSectionStations(road).Count().ToString(); } catch { }

            int surf = CountSurfaces(road);
            if (surf >= 0) d.Surfaces = surf.ToString();

            return d;
        }

        // Число режущих поверхностей (поперечники+профиль) — best-effort, свойства могут
        // отсутствовать в 60.11 -> «—» (возврат -1).
        private static int CountSurfaces(RoadAlignment road)
        {
            int total = -1;
            foreach (string prop in new[] { "SectionCuttingSurfacesRelativePaths", "ProfileCuttingSurfacesRelativePaths" })
            {
                object v = TryGet(road, prop);
                if (v is System.Collections.IEnumerable en && !(v is string))
                {
                    int c = 0;
                    foreach (var _ in en) c++;
                    total = (total < 0 ? 0 : total) + c;
                }
            }
            return total;
        }

        private static string Pk(double station)
        {
            int pk = (int)Math.Floor(station / 100.0);
            double rem = station - pk * 100.0;
            return "ПК" + pk + "+" + rem.ToString("F2");
        }

        private static double? TryDouble(object o, string name)
        {
            object v = TryGet(o, name);
            if (v is double dd) return dd;
            if (v != null && double.TryParse(v.ToString(), out double p)) return p;
            return null;
        }

        private static object TryGet(object o, string name)
        {
            if (o == null) return null;
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.GetIndexParameters().Length == 0)
                    try { return p.GetValue(o); } catch { return null; }
            }
            return null;
        }
    }
}
