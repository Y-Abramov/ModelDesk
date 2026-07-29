using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Topomatic.ApplicationPlatform;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.Cad.View;
using Topomatic.Controls.Dialogs;
using Topomatic.FoundationClasses;

namespace ModelDesk
{
    public partial class ModelDeskPlugin : PluginInitializator
    {
        public override void Initialize(PluginFactory factory)
        {
            base.Initialize(factory);
            // Бутстрап AbrModules (Shared\Bootstrap): нет своего логгера - null-делегаты.
            try { Abr.Bootstrap.AbrBootstrap.Attach("Диспетчер моделей", null, null); }
            catch { }
        }

        [cmd("switch_active_model")]
        public void SwitchActiveModel()
        {
            CadView cadView = ((PluginInitializator)this).CadView;
            if (cadView == null)
            {
                MessageDlg.Show("Нет активного видового экрана.");
                return;
            }
            object obj;
            try
            {
                obj = ((SelectionSet)cadView.SelectionSet).PickOneObjectAtScreen(
                    (Predicate<object>)((object o) => true),
                    "Кликните на объект для активации модели (Esc — отмена)");
            }
            catch (ArgumentOutOfRangeException)
            {
                MessageDlg.Show("Кликните на объект в активном видовом экране плана.");
                return;
            }
            if (obj == null) return;

            IProjectModel model = FindModelByObject(obj);
            if (model == null)
            {
                MessageDlg.Show(
                    "Не удалось определить модель для выбранного объекта.\nТип: " +
                    obj.GetType().FullName);
                return;
            }

            if (!TryActivateModel(model))
            {
                string dumpPath = Path.Combine(Path.GetTempPath(), "robur_model_dump.txt");
                DumpModelProps(model, dumpPath);
                MessageDlg.Show(
                    "Не удалось активировать модель.\nДиагностика сохранена:\n" + dumpPath);
            }
        }

        [cmd("hide_clicked_model")]
        public void HideClickedModel()
        {
            CadView cadView = ((PluginInitializator)this).CadView;
            if (cadView == null)
            {
                MessageDlg.Show("Нет активного видового экрана.");
                return;
            }
            object obj;
            try
            {
                obj = ((SelectionSet)cadView.SelectionSet).PickOneObjectAtScreen(
                    (Predicate<object>)((object o) => true),
                    "Кликните на объект для скрытия модели (Esc — отмена)");
            }
            catch (ArgumentOutOfRangeException)
            {
                MessageDlg.Show("Кликните на объект в активном видовом экране плана.");
                return;
            }
            if (obj == null) return;

            IProjectModel model = FindModelByObject(obj);
            if (model == null)
                MessageDlg.Show("Не удалось определить модель.\nТип: " + obj.GetType().FullName);
            else
                TryCloseModel(model);
        }

        [cmd("isolate_model")]
        public void IsolateModel()
        {
            CadView cadView = ((PluginInitializator)this).CadView;
            if (cadView == null)
            {
                MessageDlg.Show("Нет активного видового экрана.");
                return;
            }
            object obj;
            try
            {
                obj = ((SelectionSet)cadView.SelectionSet).PickOneObjectAtScreen(
                    (Predicate<object>)((object o) => true),
                    "Кликните на объект для изоляции модели (Esc — отмена)");
            }
            catch (ArgumentOutOfRangeException)
            {
                MessageDlg.Show("Кликните на объект в активном видовом экране плана.");
                return;
            }
            if (obj == null) return;

            IProjectModel target = FindModelByObject(obj);
            if (target == null)
            {
                MessageDlg.Show("Не удалось определить модель.\nТип: " + obj.GetType().FullName);
                return;
            }

            var toClose = new List<IProjectModel>();
            PluginCoreOps.FilterOpenedModels((Predicate<IProjectModel>)delegate(IProjectModel pm)
            {
                if (pm != target) toClose.Add(pm);
                return false;
            });
            foreach (var item in toClose)
                TryCloseModel(item);
        }

        [cmd("hide_all_models")]
        public void HideAllModels()
        {
            var toClose = new List<IProjectModel>();
            PluginCoreOps.FilterOpenedModels((Predicate<IProjectModel>)delegate(IProjectModel pm)
            {
                toClose.Add(pm);
                return false;
            });
            foreach (var item in toClose)
                TryCloseModel(item);
        }

        [cmd("show_all_models")]
        public void ShowAllModels()
        {
            IApplicationHost host = ApplicationHost.Current;
            Project proj = host?.ActiveProject;
            ModelProject mp = (proj is ModelProject mp2) ? mp2 : null;
            if (mp?.Model != null)
                OpenAllNodes(mp.Model);
        }

        [cmd("about_modeldesk")]
        public void AboutModelDesk()
        {
            using (var dlg = new AboutDialog())
                dlg.ShowDialog();
        }

        private static ModelManagerDialog _managerInstance;

        [cmd("open_model_manager")]
        public void OpenModelManager()
        {
            if (_managerInstance != null && !_managerInstance.IsDisposed)
            {
                _managerInstance.Activate();
                return;
            }
            _managerInstance = new ModelManagerDialog();
            _managerInstance.FormClosed += (s, e) => _managerInstance = null;
            _managerInstance.Show(new RoburMainWindow());
        }

        private sealed class RoburMainWindow : IWin32Window
        {
            public IntPtr Handle => Process.GetCurrentProcess().MainWindowHandle;
        }

        // ── Проводник: окно объекта «Дорога» ─────────────────────────────────

        private static readonly Dictionary<string, RoadObjectForm> _roadForms =
            new Dictionary<string, RoadObjectForm>(StringComparer.Ordinal);

        // Карточка модели (сводка + ведомости) - открыта.
        // Конструкция и копирование конструкции - сырая часть Проводника, из релиза
        // скрыта с 2026-07-26; вернуть в true при готовности. static readonly
        // (не const) - чтобы не ловить CS0162 "недостижимый код" на гейтах ниже.
        internal static readonly bool ModelCardEnabled        = true;
        internal static readonly bool RoadConstructionEnabled = false;

        // Открывает (или выводит на передний план) окно дороги для модели.
        // Вызывается кнопкой диспетчера и командами ленты.
        internal static void OpenRoadForm(IProjectModel model, RoadObjectForm.Tab? tab)
        {
            if (!ModelCardEnabled) { MessageDlg.Show("Карточка модели в разработке."); return; }
            if (model == null) return;
            if (!RoadModelAccess.TryGetRoadAlignment(model, out var road))
            {
                MessageDlg.Show("Это не дорожная модель.");
                return;
            }
            string name = PluginCoreOps.GetFileName(model);
            if (!string.IsNullOrEmpty(name))
                name = Path.GetFileNameWithoutExtension(name);
            string key = PluginCoreOps.FindModelPathId(model) ?? name ?? road.GetHashCode().ToString();

            if (_roadForms.TryGetValue(key, out var existing) && existing != null && !existing.IsDisposed)
            {
                existing.Activate();
                if (tab.HasValue) existing.SelectTab(tab.Value);
                return;
            }

            var form = new RoadObjectForm(road, name, key);
            _roadForms[key] = form;
            form.FormClosed += (s, e) => _roadForms.Remove(key);
            form.Show(new RoburMainWindow());
            if (tab.HasValue) form.SelectTab(tab.Value);
        }

        // Первая открытая дорожная модель проекта (для команд без явного выбора).
        private static IProjectModel FindFirstRoadModel()
        {
            IProjectModel result = null;
            PluginCoreOps.FilterOpenedModels((Predicate<IProjectModel>)delegate (IProjectModel pm)
            {
                if (result == null && RoadModelAccess.TryGetRoadAlignment(pm, out _)) result = pm;
                return false;
            });
            return result;
        }

        [cmd("open_road_object")]
        public void OpenRoadObject() => OpenActiveRoadOn(null);

        [cmd("road_construction")]
        public void RoadConstruction()
        {
            if (!RoadConstructionEnabled) { MessageDlg.Show("Проводник дороги в разработке."); return; }
            OpenActiveRoadOn(RoadObjectForm.Tab.Construction);
        }

        [cmd("road_copy_construction")]
        public void RoadCopyConstruction()
        {
            if (!RoadConstructionEnabled) { MessageDlg.Show("Проводник дороги в разработке."); return; }
            IProjectModel model = FindFirstRoadModel();
            if (model == null) { MessageDlg.Show("Нет открытой дороги."); return; }
            if (!RoadModelAccess.TryGetRoadAlignment(model, out var road))
            { MessageDlg.Show("Это не дорожная модель."); return; }
            var targets = RoadObjectForm.CollectTargetRoadsFor(road);
            if (targets.Count == 0)
            { MessageDlg.Show("Нет других открытых дорог для копирования."); return; }
            using (var dlg = new CopyConstructionDialog(road, targets))
                dlg.ShowDialog(new RoburMainWindow());
        }

        private void OpenActiveRoadOn(RoadObjectForm.Tab? tab)
        {
            IProjectModel model = FindFirstRoadModel();
            if (model == null) { MessageDlg.Show("Нет открытой дороги."); return; }
            OpenRoadForm(model, tab);
        }

        // ── Карточка модели произвольного типа ───────────────────────────────

        private static readonly Dictionary<string, ModelCardForm> _cardForms =
            new Dictionary<string, ModelCardForm>(StringComparer.Ordinal);

        // Открывает карточку модели. Дорожные модели идут в RoadObjectForm
        // (там своя богатая «Сводка»), остальные - в ModelCardForm.
        internal static void OpenModelCard(IProjectModel model, bool onSheets)
        {
            if (!ModelCardEnabled) { MessageDlg.Show("Карточка модели в разработке."); return; }
            if (model == null) { MessageDlg.Show("Модель не выбрана."); return; }

            if (RoadModelAccess.TryGetRoadAlignment(model, out _))
            {
                OpenRoadForm(model, onSheets ? RoadObjectForm.Tab.Sheets : RoadObjectForm.Tab.Summary);
                return;
            }

            string file = PluginCoreOps.GetFileName(model);
            string name = string.IsNullOrEmpty(file) ? "модель" : Path.GetFileNameWithoutExtension(file);
            string key  = PluginCoreOps.FindModelPathId(model) ?? name;

            ModelCardForm existing;
            if (_cardForms.TryGetValue(key, out existing) && existing != null && !existing.IsDisposed)
            {
                existing.Activate();
                existing.SelectTab(onSheets ? ModelCardForm.Tab.Sheets : ModelCardForm.Tab.Summary);
                return;
            }

            var props = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Имя",  name),
                new KeyValuePair<string, string>("Файл", file ?? string.Empty),
                new KeyValuePair<string, string>("Идентификатор", key),
                new KeyValuePair<string, string>("URI",  model.Uri == null ? string.Empty : model.Uri.ToString())
            };

            var form = new ModelCardForm(name, key, props);
            _cardForms[key] = form;
            form.FormClosed += (s, e) => _cardForms.Remove(key);
            form.Show(new RoburMainWindow());
            form.SelectTab(onSheets ? ModelCardForm.Tab.Sheets : ModelCardForm.Tab.Summary);
        }

        // Диспетчер знает про выделение в списке - команды делегируют ему,
        // когда он открыт. Иначе работаем по первой открытой дороге.
        internal static ModelManagerDialog OpenManager
        {
            get { return _managerInstance != null && !_managerInstance.IsDisposed ? _managerInstance : null; }
        }

        [cmd("open_model_card")]
        public void OpenModelCardCommand() => DispatchCard(false);

        [cmd("model_make_sheet")]
        public void MakeSheetCommand() => DispatchCard(true);

        private static void DispatchCard(bool onSheets)
        {
            var mgr = OpenManager;
            if (mgr != null && mgr.TryOpenCardForSelected(onSheets)) return;

            IProjectModel model = FindFirstRoadModel();
            if (model == null)
            {
                MessageDlg.Show("Откройте диспетчер моделей и выберите модель.");
                return;
            }
            OpenModelCard(model, onSheets);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void OpenAllNodes(IProjectModel node)
        {
            if (node == null) return;
            string pathId = PluginCoreOps.FindModelPathId(node);
            if (!string.IsNullOrEmpty(pathId))
                TryExecute("open", new object[] { pathId });
            IProjectModel[] children = node.GetChilds();
            if (children != null)
                foreach (var child in children)
                    OpenAllNodes(child);
        }

        private IProjectModel FindModelByObject(object obj)
        {
            if (obj == null) return null;

            IProjectModel found = PluginCoreOps.FindModel(obj);
            if (found != null) return found;

            IWrapped wrapped = obj as IWrapped;
            if (wrapped?.WrappedObject != null)
            {
                found = PluginCoreOps.FindModel(wrapped.WrappedObject);
                if (found != null) return found;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string name in new[] { "WrappedObject", "Alignment", "Surface", "Model", "Owner", "Parent" })
            {
                object val = GetPropValue(obj, name, flags);
                if (val != null && val != obj)
                {
                    found = PluginCoreOps.FindModel(val);
                    if (found != null) return found;
                }
            }
            return null;
        }

        // Используем подтверждённые команды Robur: activate, close, open
        private bool TryActivateModel(IProjectModel pm)
        {
            string pathId = PluginCoreOps.FindModelPathId(pm);
            if (!string.IsNullOrEmpty(pathId) && TryExecute("activate", new object[] { pathId }))
                return true;
            return TryExecute("activate", new object[] { pm });
        }

        private void TryCloseModel(IProjectModel pm)
        {
            string pathId = PluginCoreOps.FindModelPathId(pm);
            if (!string.IsNullOrEmpty(pathId))
                TryExecute("close", new object[] { pathId });
            else
                TryExecute("close", new object[] { pm });
        }

        internal static bool TryExecute(string cmd, object[] args)
        {
            try
            {
                ApplicationHost.Current.Plugins.Execute(cmd, args);
                return true;
            }
            catch { return false; }
        }

        private object GetPropValue(object obj, string name, BindingFlags flags)
        {
            if (obj == null) return null;
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                PropertyInfo p = t.GetProperty(name, flags);
                if (p != null)
                    try { return p.GetValue(obj); } catch { return null; }
            }
            return null;
        }

        private void DumpModelProps(IProjectModel pm, string path)
        {
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var lines = new List<string>();
            lines.Add("=== " + ((object)pm).GetType().FullName + " ===");
            foreach (var p in ((object)pm).GetType().GetProperties(flags))
                try { lines.Add("PROP  " + p.Name + " = " + p.GetValue(pm)); }
                catch (Exception ex) { lines.Add("PROP  " + p.Name + " ERR: " + ex.Message); }
            foreach (var f in ((object)pm).GetType().GetFields(flags))
                try { lines.Add("FIELD " + f.Name + " = " + f.GetValue(pm)); }
                catch (Exception ex) { lines.Add("FIELD " + f.Name + " ERR: " + ex.Message); }
            File.WriteAllLines(path, lines, Encoding.UTF8);
        }
    }
}
