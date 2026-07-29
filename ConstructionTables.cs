using System;
using System.Collections;
using System.Collections.Generic;
using Topomatic.Alg.Road;
using Topomatic.Alg.Road.Urb;
using Topomatic.Alg.Road.Urb.GeneralStrips;

namespace ModelDesk
{
    // Одна таблица «Верха проектной конструкции» (RoadAlignment.Urb): живой IList строк-struct
    // + делегаты чтения/пересборки. Строки — value-type struct, поэтому правка = клон struct
    // с изменённым полем + пересборка IList (WithStation/WithField возвращают boxed-копию).
    // Поля подтверждены рефлексией SDK 16.0.60.11 (namespace ...Urb.GeneralStrips),
    // см. reference_robur_urb_construction_api.
    internal sealed class ConstructionTable
    {
        public string Id;
        public string Title;
        public IList Rows;                          // живой IList<T> из Urb (List<T>: реализует IList)
        public string[] Columns;                    // заголовки (Пикет + поля)
        public string[] FieldNames;                 // имена числовых полей (без Пикета)
        public Func<object, string[]> Cells;        // строка -> ячейки (Пикет + поля), формат F3
        public Func<object, double> GetStation;
        public Func<object, double, object> WithStation;      // клон struct с новой станцией
        public Func<object, int, double> GetField;            // поле по индексу (0..FieldNames.Length-1)
        public Func<object, int, double, object> WithField;   // клон struct с полем[i]=value
    }

    // Читает 5 под-таблиц верха конструкции дороги в единый список ConstructionTable.
    // Ids (widths/grades/sides/divider/construction) — контракт для копира/пресетов/пакетной правки.
    internal static class ConstructionTables
    {
        internal static IReadOnlyList<ConstructionTable> Read(RoadAlignment road)
        {
            var tables = new List<ConstructionTable>();
            UrbParams urb = road?.Urb;
            if (urb == null) return tables;

            AddMain(tables, "widths", "Ширины основных полос", urb.MainStrips?.Widths);
            AddMain(tables, "grades", "Уклоны основных полос", urb.MainStrips?.Grades);
            AddSide(tables, urb.SideStrips?.Widths);
            AddDivider(tables, urb.DividerStrips?.Widths);
            AddConstruction(tables, urb.Construction?.ConstructionItems);
            return tables;
        }

        private static string F(double v) => v.ToString("F3");

        private static string[] Prepend(string h, string[] rest)
        {
            var a = new string[rest.Length + 1];
            a[0] = h;
            Array.Copy(rest, 0, a, 1, rest.Length);
            return a;
        }

        // ── Ширины / уклоны основных полос (MainItem) ────────────────────────
        private static void AddMain(List<ConstructionTable> t, string id, string title, IList<MainItem> list)
        {
            if (list == null) return;
            var names = new[] { "Л1", "Л2", "Л3", "Л4", "Л5", "П1", "П2", "П3", "П4", "П5" };
            t.Add(new ConstructionTable
            {
                Id = id, Title = title, Rows = (IList)list,
                Columns = Prepend("Пикет", names), FieldNames = names,
                Cells = o => { var m = (MainItem)o; return new[] { F(m.Station),
                    F(m.Left1), F(m.Left2), F(m.Left3), F(m.Left4), F(m.Left5),
                    F(m.Right1), F(m.Right2), F(m.Right3), F(m.Right4), F(m.Right5) }; },
                GetStation = o => ((MainItem)o).Station,
                WithStation = (o, st) => { var m = (MainItem)o; m.Station = st; return m; },
                GetField = (o, i) => MainField((MainItem)o, i),
                WithField = (o, i, v) => MainWith((MainItem)o, i, v),
            });
        }
        private static double MainField(MainItem m, int i)
        {
            switch (i)
            {
                case 0: return m.Left1; case 1: return m.Left2; case 2: return m.Left3;
                case 3: return m.Left4; case 4: return m.Left5;
                case 5: return m.Right1; case 6: return m.Right2; case 7: return m.Right3;
                case 8: return m.Right4; default: return m.Right5;
            }
        }
        private static object MainWith(MainItem m, int i, double v)
        {
            switch (i)
            {
                case 0: m.Left1 = v; break; case 1: m.Left2 = v; break; case 2: m.Left3 = v; break;
                case 3: m.Left4 = v; break; case 4: m.Left5 = v; break;
                case 5: m.Right1 = v; break; case 6: m.Right2 = v; break; case 7: m.Right3 = v; break;
                case 8: m.Right4 = v; break; default: m.Right5 = v; break;
            }
            return m;
        }

        // ── Обочины (SideItem) ───────────────────────────────────────────────
        private static void AddSide(List<ConstructionTable> t, IList<SideItem> list)
        {
            if (list == null) return;
            var names = new[] { "Л полн.", "Л грунт", "Л укреп.", "П полн.", "П грунт", "П укреп." };
            t.Add(new ConstructionTable
            {
                Id = "sides", Title = "Обочины", Rows = (IList)list,
                Columns = Prepend("Пикет", names), FieldNames = names,
                Cells = o => { var s = (SideItem)o; return new[] { F(s.Station),
                    F(s.LeftFull), F(s.LeftGrass), F(s.LeftConcrete),
                    F(s.RightFull), F(s.RightGrass), F(s.RightConcrete) }; },
                GetStation = o => ((SideItem)o).Station,
                WithStation = (o, st) => { var s = (SideItem)o; s.Station = st; return s; },
                GetField = (o, i) => SideField((SideItem)o, i),
                WithField = (o, i, v) => SideWith((SideItem)o, i, v),
            });
        }
        private static double SideField(SideItem s, int i)
        {
            switch (i)
            {
                case 0: return s.LeftFull; case 1: return s.LeftGrass; case 2: return s.LeftConcrete;
                case 3: return s.RightFull; case 4: return s.RightGrass; default: return s.RightConcrete;
            }
        }
        private static object SideWith(SideItem s, int i, double v)
        {
            switch (i)
            {
                case 0: s.LeftFull = v; break; case 1: s.LeftGrass = v; break; case 2: s.LeftConcrete = v; break;
                case 3: s.RightFull = v; break; case 4: s.RightGrass = v; break; default: s.RightConcrete = v; break;
            }
            return s;
        }

        // ── Разделительная (DividerItem) ─────────────────────────────────────
        private static void AddDivider(List<ConstructionTable> t, IList<DividerItem> list)
        {
            if (list == null) return;
            var names = new[] { "Л полн.", "Л борт", "П полн.", "П борт" };
            t.Add(new ConstructionTable
            {
                Id = "divider", Title = "Разделительная", Rows = (IList)list,
                Columns = Prepend("Пикет", names), FieldNames = names,
                Cells = o => { var d = (DividerItem)o; return new[] { F(d.Station),
                    F(d.LeftFull), F(d.LeftBorder), F(d.RightFull), F(d.RightBorder) }; },
                GetStation = o => ((DividerItem)o).Station,
                WithStation = (o, st) => { var d = (DividerItem)o; d.Station = st; return d; },
                GetField = (o, i) => DividerField((DividerItem)o, i),
                WithField = (o, i, v) => DividerWith((DividerItem)o, i, v),
            });
        }
        private static double DividerField(DividerItem d, int i)
        {
            switch (i)
            {
                case 0: return d.LeftFull; case 1: return d.LeftBorder;
                case 2: return d.RightFull; default: return d.RightBorder;
            }
        }
        private static object DividerWith(DividerItem d, int i, double v)
        {
            switch (i)
            {
                case 0: d.LeftFull = v; break; case 1: d.LeftBorder = v; break;
                case 2: d.RightFull = v; break; default: d.RightBorder = v; break;
            }
            return d;
        }

        // ── Конструкция (слои, ConstructionItem — [Obsolete] в 60.11, работает) ─
#pragma warning disable 618
        private static void AddConstruction(List<ConstructionTable> t, IList<ConstructionItem> list)
        {
            if (list == null) return;
            var names = new[] { "Асфальт", "Щебень", "Песок", "Раб.слой", "Л укл.песка", "П укл.песка", "Уширение" };
            t.Add(new ConstructionTable
            {
                Id = "construction", Title = "Конструкция (слои)", Rows = (IList)list,
                Columns = Prepend("Пикет", names), FieldNames = names,
                Cells = o => { var c = (ConstructionItem)o; return new[] { F(c.Station),
                    F(c.AsphaltLayerHeight), F(c.StoneLayerHeight), F(c.SandLayerHeight),
                    F(c.WorkingLayerHeight), F(c.LeftSandGrade), F(c.RightSandGrade), F(c.FondationBroadening) }; },
                GetStation = o => ((ConstructionItem)o).Station,
                WithStation = (o, st) => { var c = (ConstructionItem)o; c.Station = st; return c; },
                GetField = (o, i) => ConstructionField((ConstructionItem)o, i),
                WithField = (o, i, v) => ConstructionWith((ConstructionItem)o, i, v),
            });
        }
        private static double ConstructionField(ConstructionItem c, int i)
        {
            switch (i)
            {
                case 0: return c.AsphaltLayerHeight; case 1: return c.StoneLayerHeight; case 2: return c.SandLayerHeight;
                case 3: return c.WorkingLayerHeight; case 4: return c.LeftSandGrade; case 5: return c.RightSandGrade;
                default: return c.FondationBroadening;
            }
        }
        private static object ConstructionWith(ConstructionItem c, int i, double v)
        {
            switch (i)
            {
                case 0: c.AsphaltLayerHeight = v; break; case 1: c.StoneLayerHeight = v; break; case 2: c.SandLayerHeight = v; break;
                case 3: c.WorkingLayerHeight = v; break; case 4: c.LeftSandGrade = v; break; case 5: c.RightSandGrade = v; break;
                default: c.FondationBroadening = v; break;
            }
            return c;
        }
#pragma warning restore 618
    }
}
