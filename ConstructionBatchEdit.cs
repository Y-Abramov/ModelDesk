using System.Collections.Generic;
using System.Linq;
using Topomatic.Alg.Road;

namespace ModelDesk
{
    // Пакетная правка: задать одно поле таблицы во всех строках диапазона станций
    // [from,to] (null-граница = без ограничения) или по всей дороге. Транзакционно.
    // Строки — value-type struct, поэтому запись = пересборка IList клонами.
    internal static class ConstructionBatchEdit
    {
        internal static void SetField(RoadAlignment road, string tableId, int fieldIndex,
                                      double? from, double? to, double value)
        {
            var t = ConstructionTables.Read(road).FirstOrDefault(x => x.Id == tableId);
            if (t == null) return;                       // неизвестный tableId -> no-op
            var urb = road.Urb;
            urb.BeginUpdate("Пакетная правка конструкции");
            try
            {
                var rebuilt = new List<object>(t.Rows.Count);
                foreach (var o in t.Rows)
                {
                    double s = t.GetStation(o);
                    bool inRange = (!from.HasValue || s >= from.Value) && (!to.HasValue || s <= to.Value);
                    rebuilt.Add(inRange ? t.WithField(o, fieldIndex, value) : o);
                }
                t.Rows.Clear();
                foreach (var r in rebuilt) t.Rows.Add(r);
                urb.EndUpdate();
            }
            catch { try { urb.EndUpdate(); } catch { } throw; }
        }
    }
}
