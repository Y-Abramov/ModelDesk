using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ModelDesk
{
    // Чистый (без ссылок на Robur) разбор каталога ведомостей из текста .plugin-файлов.
    //
    // GenerateMenu живьём вернул пустое дерево при вызове вне штатного построения
    // меню (см. ModelView/CLAUDE.md, "Ведомости Robur"). Родные .plugin-файлы при
    // этом уже содержат всё нужное открытым текстом: "actions" (id -> cmd/title/
    // description) и "menubars"["rbproj.project.mksht"] (id -> priority). Проблема
    // в том, что один и тот же ключ "rbproj.project.mksht" повторяется в одном
    // "menubars" по несколько раз (разные .plugin-секции одного файла) - обычный
    // JObject-парсер молча оставил бы только последнее вхождение. JParser ниже
    // хранит объект как список пар (ключ, значение), а не Dictionary, поэтому
    // дубликаты не теряются.
    internal static class RoburSheetCatalog
    {
        internal sealed class ActionDef
        {
            public string Cmd;
            public string Title;
            public string Description;
        }

        // Разбирает один .plugin-файл (текст), докладывает найденные действия в
        // `actions` (id -> ActionDef) и листья нужного меню в `collected` (id, priority).
        // Разбор best-effort: некорректный JSON или отсутствие секций - тихий no-op.
        internal static void Ingest(string json, string menuUid,
                                    Dictionary<string, ActionDef> actions,
                                    List<(string id, double priority)> collected)
        {
            JVal root;
            try { root = JParser.Parse(json); }
            catch { return; }

            if (root == null || root.Kind != JKind.Object) return;

            foreach (var top in root.Members)
            {
                if (top.Value == null) continue;

                if (top.Key == "actions" && top.Value.Kind == JKind.Object)
                    IngestActions(top.Value, actions);
                else if (top.Key == "menubars" && top.Value.Kind == JKind.Object)
                    IngestMenubars(top.Value, menuUid, collected);
            }
        }

        private static void IngestActions(JVal actionsObj, Dictionary<string, ActionDef> actions)
        {
            foreach (var a in actionsObj.Members)
            {
                if (a.Value == null || a.Value.Kind != JKind.Object) continue;
                if (actions.ContainsKey(a.Key)) continue; // первое определение побеждает

                var def = new ActionDef();
                foreach (var f in a.Value.Members)
                {
                    if (f.Value == null || f.Value.Kind != JKind.String) continue;
                    if (f.Key == "cmd") def.Cmd = f.Value.Str;
                    else if (f.Key == "title") def.Title = f.Value.Str;
                    else if (f.Key == "description") def.Description = f.Value.Str;
                }
                if (!string.IsNullOrEmpty(def.Cmd)) actions[a.Key] = def;
            }
        }

        private static void IngestMenubars(JVal menubarsObj, string menuUid,
                                            List<(string id, double priority)> collected)
        {
            foreach (var mb in menubarsObj.Members)
            {
                // Точное совпадение ключа: варианты вроде "rbproj.project.mksht.multiply"
                // (пакетная обработка нескольких моделей) сюда не подходят - v1 работает
                // с одной активной моделью.
                if (mb.Key != menuUid || mb.Value == null) continue;

                JVal itemsArr = null;
                double priority = 0;

                if (mb.Value.Kind == JKind.Array)
                {
                    itemsArr = mb.Value;
                }
                else if (mb.Value.Kind == JKind.Object)
                {
                    foreach (var f in mb.Value.Members)
                    {
                        if (f.Key == "items" && f.Value != null && f.Value.Kind == JKind.Array)
                            itemsArr = f.Value;
                        else if (f.Key == "priority" && f.Value != null && f.Value.Kind == JKind.Number)
                            priority = f.Value.Num;
                    }
                }
                if (itemsArr == null) continue;

                foreach (var it in itemsArr.Items)
                {
                    // Вложенные объекты - подменю/группы (напр. "occupied_area" в
                    // extentions.plugin) - v1 не разворачивает, только плоские пункты.
                    if (it.Kind != JKind.String) continue;

                    string id = it.Str;
                    if (string.IsNullOrEmpty(id) || id == "-") continue;
                    collected.Add((id, priority));
                }
            }
        }

        // Некоторые действия кодируют аргумент прямо в "cmd" по конвенции Robur:
        // `"cmd": "sheet_smdx_entity \"SmdxEntity\""` - имя команды + пробел +
        // один строковый аргумент в кавычках (см. alg_sheets.plugin, id_sheet_smdx_*).
        // PluginManager.Execute(uid, args) команду и аргумент принимает раздельно,
        // поэтому перед вызовом это нужно расщепить.
        internal static void SplitCmd(string raw, out string uid, out string arg)
        {
            uid = raw;
            arg = null;
            if (string.IsNullOrEmpty(raw)) return;

            int sp = raw.IndexOf(' ');
            if (sp < 0) return;

            string rest = raw.Substring(sp + 1).Trim();
            if (rest.Length >= 2 && rest[0] == '"' && rest[rest.Length - 1] == '"')
            {
                uid = raw.Substring(0, sp);
                arg = rest.Substring(1, rest.Length - 2);
            }
        }

        // Резолвит собранные id в действия и строит плоский каталог (один корень,
        // все пункты - его прямые дети). Порядок - по priority (больше = выше,
        // как в ribbon .plugin проекта). Нерезолвящиеся id - тихо пропускаются,
        // а не роняют каталог.
        internal static SheetNode BuildCatalog(Dictionary<string, ActionDef> actions,
                                                List<(string id, double priority)> collected)
        {
            var root = new SheetNode();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in collected.OrderByDescending(x => x.priority))
            {
                if (!seen.Add(entry.id)) continue;

                ActionDef def;
                if (!actions.TryGetValue(entry.id, out def))
                {
                    // Cross-file ссылки вида "namespace.id" (см. alg_sheets.plugin ->
                    // "arrangements_sheets.id_rsf_sheet_work") - действие определено
                    // без префикса в своём файле (arrangements_sheets.plugin).
                    int dot = entry.id.LastIndexOf('.');
                    if (dot < 0 || !actions.TryGetValue(entry.id.Substring(dot + 1), out def))
                        continue;
                }
                if (string.IsNullOrEmpty(def.Cmd)) continue;

                root.Children.Add(new SheetNode
                {
                    Text    = def.Title ?? def.Cmd,
                    Hint    = def.Description,
                    Enabled = true,
                    Tag     = def.Cmd
                });
            }
            return root;
        }
    }

    // ── Минимальный толерантный JSON-парсер ─────────────────────────────────
    //
    // System.Text.Json/JObject молча теряет повторяющиеся ключи объекта (берёт
    // последний) - а именно так устроены родные .plugin Robur (см. Ingest выше).
    // Members объекта здесь - List<KeyValuePair>, а не Dictionary: дубликаты ключей
    // сохраняются в порядке появления. Только для чтения, без внешних зависимостей
    // (в проекте уже избегают Newtonsoft.Json - см. ModelDeskSettings).

    internal enum JKind { Object, Array, String, Number, Bool, Null }

    internal sealed class JVal
    {
        internal JKind Kind;
        internal string Str;
        internal double Num;
        internal bool Bool;
        internal List<KeyValuePair<string, JVal>> Members;
        internal List<JVal> Items;
    }

    internal static class JParser
    {
        internal static JVal Parse(string s)
        {
            int i = 0;
            return ParseValue(s, ref i);
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        private static JVal ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");

            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return new JVal { Kind = JKind.String, Str = ParseString(s, ref i) };
            if (c == 't') { Expect(s, ref i, "true");  return new JVal { Kind = JKind.Bool, Bool = true }; }
            if (c == 'f') { Expect(s, ref i, "false"); return new JVal { Kind = JKind.Bool, Bool = false }; }
            if (c == 'n') { Expect(s, ref i, "null");  return new JVal { Kind = JKind.Null }; }
            return ParseNumber(s, ref i);
        }

        private static void Expect(string s, ref int i, string lit)
        {
            if (i + lit.Length > s.Length || string.CompareOrdinal(s.Substring(i, lit.Length), lit) != 0)
                throw new FormatException("Expected literal " + lit);
            i += lit.Length;
        }

        private static JVal ParseObject(string s, ref int i)
        {
            i++; // '{'
            var members = new List<KeyValuePair<string, JVal>>();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return new JVal { Kind = JKind.Object, Members = members }; }

            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':'");
                i++;
                var val = ParseValue(s, ref i);
                members.Add(new KeyValuePair<string, JVal>(key, val));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                throw new FormatException("Expected ',' or '}'");
            }
            return new JVal { Kind = JKind.Object, Members = members };
        }

        private static JVal ParseArray(string s, ref int i)
        {
            i++; // '['
            var items = new List<JVal>();
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return new JVal { Kind = JKind.Array, Items = items }; }

            while (true)
            {
                items.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                throw new FormatException("Expected ',' or ']'");
            }
            return new JVal { Kind = JKind.Array, Items = items };
        }

        private static string ParseString(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"') throw new FormatException("Expected '\"'");
            i++;

            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated string");
                char c = s[i++];
                if (c == '"') break;

                if (c == '\\')
                {
                    if (i >= s.Length) throw new FormatException("Unterminated escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("Bad unicode escape");
                            int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            i += 4;
                            sb.Append((char)code);
                            break;
                        default: throw new FormatException("Bad escape \\" + e);
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static JVal ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
                i++;

            string num = s.Substring(start, i - start);
            double d;
            double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            return new JVal { Kind = JKind.Number, Num = d };
        }
    }
}
