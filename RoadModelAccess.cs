using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Topomatic.Alg;              // Alignment
using Topomatic.Alg.Crs;          // SelectedSectionsCollection, SelectedSections
using Topomatic.Alg.Model;        // AlignmentModel
using Topomatic.Alg.Road;         // RoadAlignment
using Topomatic.Alg.Road.Urb;     // UrbParams
using Topomatic.ApplicationPlatform;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;

namespace ModelDesk
{
    // Мост IProjectModel/активная дорога -> RoadAlignment + доступ к станциям поперечников.
    //
    // Путь доступа подтверждён по существующему коду (SwitchModelCommand):
    //   IProjectModel.LockRead() -> AlignmentModel; AlignmentModel.Alignment -> Alignment;
    //   для дороги Alignment == RoadAlignment.
    // Члены SelectedSections подтверждены рефлексией SDK 16.0.60.11
    // (Topomatic.Alg.Crs): коллекция named-наборов SelectedSectionsCollection.Items =
    // IEnumerable<KeyValuePair<string, SelectedSections>>; набор SelectedSections:
    //   int Count; double this[int i] (станция по индексу, read-only);
    //   bool this[double station] (флаг выбора, read+write); AddSection(double); Clear().
    //
    // ЖИВОЙ ГЕЙТ (Task 1/10): семантика снятия выбора (set[station]=false — deselect
    // или физическое удаление; влияет ли на Count) подтверждается только на реальном
    // проекте. Здесь реализовано как снятие выбора (мягкая деградация).
    internal static class RoadModelAccess
    {
        internal static bool TryGetRoadAlignment(IProjectModel model, out RoadAlignment road)
        {
            road = null;
            if (model == null) return false;
            try
            {
                // IProjectModel.LockRead() -> data-объект; парного UnlockRead в SDK нет
                // (существующий код SwitchModelCommand читает так же, без снятия).
                object data = model.LockRead();
                AlignmentModel am = data as AlignmentModel;
                Alignment al = am?.Alignment;
                road = al as RoadAlignment;
            }
            catch { }
            return road != null;
        }

        // v1: «активная дорога» = первая дорожная модель среди открытых.
        // Двойной клик в диспетчере передаёт конкретную модель (точнее) — это fallback
        // для команд ленты без явного выбора.
        internal static bool TryGetActiveRoad(out RoadAlignment road)
        {
            RoadAlignment found = null;
            try
            {
                PluginCoreOps.FilterOpenedModels((Predicate<IProjectModel>)delegate (IProjectModel pm)
                {
                    if (found == null && TryGetRoadAlignment(pm, out RoadAlignment r)) found = r;
                    return false;
                });
            }
            catch { }
            road = found;
            return road != null;
        }

        // Перечисляет станции поперечников по всем named-наборам SelectedSections.
        internal static IEnumerable<double> GetSectionStations(RoadAlignment road)
        {
            var result = new List<double>();
            SelectedSectionsCollection coll = road?.SelectedSections;
            if (coll == null) return result;
            try
            {
                foreach (KeyValuePair<string, SelectedSections> kv in coll.Items)
                {
                    SelectedSections set = kv.Value;
                    if (set == null) continue;
                    int n = set.Count;
                    for (int i = 0; i < n; i++)
                        result.Add(set[i]);   // double this[int]
                }
            }
            catch { }
            return result;
        }

        // Снимает выбор указанных станций во всех наборах, транзакционно (штатный Undo).
        // Снимок станций набора берётся до правки — устойчиво к возможной переиндексации.
        internal static void RemoveSections(RoadAlignment road, IEnumerable<double> stations)
        {
            SelectedSectionsCollection coll = road?.SelectedSections;
            if (coll == null) return;
            var targets = new List<double>(stations ?? new double[0]);
            if (targets.Count == 0) return;

            coll.BeginUpdate("Прореживание поперечников");
            try
            {
                foreach (KeyValuePair<string, SelectedSections> kv in coll.Items)
                {
                    SelectedSections set = kv.Value;
                    if (set == null) continue;
                    var live = new List<double>(set.Count);
                    for (int i = 0; i < set.Count; i++) live.Add(set[i]);
                    foreach (double st in live)
                        if (ContainsApprox(targets, st))
                            set[st] = false;   // bool this[double] write -> снять выбор
                }
                coll.EndUpdate();
            }
            catch
            {
                try { coll.EndUpdate(); } catch { }
                throw;
            }
        }

        private static bool ContainsApprox(List<double> list, double v)
        {
            for (int i = 0; i < list.Count; i++)
                if (Math.Abs(list[i] - v) < 1e-6) return true;
            return false;
        }

    }
}
