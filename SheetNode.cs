using System;
using System.Collections.Generic;
using System.Text;

namespace ModelDesk
{
    // Чистая модель пункта каталога ведомостей. Никаких ссылок на Robur -
    // файл линкуется в тест-проект Tests\RoadTools.Tests.
    // В рантайме Tag несёт Topomatic.Controls.MenuAction (см. SheetCatalogRobur).
    internal sealed class SheetNode
    {
        public string Text;
        public string Hint;
        public bool   Enabled;
        public bool   IsSeparator;
        public object Tag;
        public string Path;   // путь подменю, напр. "Общие ведомости"; null для верхнего уровня

        public List<SheetNode> Children = new List<SheetNode>();
    }

    internal static class SheetTree
    {
        // Заголовки ведомостей земляных работ и объёмов. Сопоставление -
        // по нормализованному тексту (Normalize), т.к. меню отдаёт только заголовок.
        internal static readonly string[] EarthworkTitles =
        {
            "Площадей и объемов",
            "Рабочая ведомость объемов",
            "Объемы по слоям",
            "Выемка с учетом геологии",
            "Срезка",
            "Ведомость земляных работ",
            "Ведомость распределения земляных масс",
            "Рабочая ведомость распределения земляных масс",
        };

        internal static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string s = text.Trim();
            while (s.Length > 0 && s[s.Length - 1] == '.') s = s.Substring(0, s.Length - 1);
            s = s.Trim();

            var sb = new StringBuilder(s.Length);
            bool prevSpace = false;
            foreach (char c in s)
            {
                bool sp = char.IsWhiteSpace(c);
                if (sp && prevSpace) continue;
                sb.Append(sp ? ' ' : c);
                prevSpace = sp;
            }
            return sb.ToString();
        }

        // Дерево -> плоский список листьев. Разделители отбрасываются,
        // порядок сохраняется, путь подменю пишется в Path.
        internal static List<SheetNode> Flatten(SheetNode root)
        {
            var result = new List<SheetNode>();
            if (root != null) Walk(root, null, result);
            return result;
        }

        private static void Walk(SheetNode node, string path, List<SheetNode> acc)
        {
            foreach (var child in node.Children)
            {
                if (child.IsSeparator) continue;

                if (child.Children.Count > 0)
                {
                    string sub = string.IsNullOrEmpty(path) ? child.Text : path + " / " + child.Text;
                    Walk(child, sub, acc);
                }
                else
                {
                    child.Path = path;
                    acc.Add(child);
                }
            }
        }

        // Делит плоский список на «Земляные работы» и «Прочие». Ни один пункт не теряется.
        internal static void SplitEarthworks(IList<SheetNode> items,
                                             out List<SheetNode> earthworks,
                                             out List<SheetNode> others)
        {
            earthworks = new List<SheetNode>();
            others     = new List<SheetNode>();
            if (items == null) return;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in EarthworkTitles) set.Add(Normalize(t));

            foreach (var n in items)
            {
                if (set.Contains(Normalize(n.Text))) earthworks.Add(n);
                else                                 others.Add(n);
            }
        }

        // Поиск по подстроке без учёта регистра. Пустой запрос -> исходный список.
        internal static List<SheetNode> Filter(IList<SheetNode> items, string query)
        {
            var result = new List<SheetNode>();
            if (items == null) return result;

            if (string.IsNullOrWhiteSpace(query)) { result.AddRange(items); return result; }

            string q = query.Trim();
            foreach (var n in items)
            {
                string hay = (n.Text ?? string.Empty) + " " +
                             (n.Hint ?? string.Empty) + " " +
                             (n.Path ?? string.Empty);
                if (hay.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(n);
            }
            return result;
        }
    }
}
