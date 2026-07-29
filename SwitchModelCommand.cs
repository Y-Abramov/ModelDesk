// SwitchModelCommand.cs
// Быстрое переключение активной модели кликом на объект в viewport

using System;
using Topomatic.Alg;
using Topomatic.Alg.Model;
using Topomatic.ApplicationPlatform;
using Topomatic.ApplicationPlatform.Core;
using Topomatic.ApplicationPlatform.Plugins;
using Topomatic.Cad.View;
using Topomatic.Controls.Dialogs;

namespace RoadStyle
{
    public partial class RoadStylePlugin : PluginInitializator
    {
        [cmd("switch_active_model")]
        public void SwitchActiveModel()
        {
            var cadView = CadView;
            if (cadView == null)
            {
                MessageDlg.Show("Нет активного вида.");
                return;
            }

            var obj = cadView.SelectionSet.PickOneObjectAtScreen(
                o => true,
                "Укажите объект модели");

            if (obj == null) return;

            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance;

            // Получаем Alignment из wrapper объекта
            Alignment alignment = GetAlignmentFromWrapper(obj, flags);

            if (alignment == null)
            {
                MessageDlg.Show("Не удалось определить трассу объекта.\nВыберите объект автомобильной дороги.");
                return;
            }

            // Находим IProjectModel у которого AlignmentModel.Alignment совпадает
            IProjectModel targetModel = null;
            PluginCoreOps.FilterOpenedModels(pm =>
            {
                try
                {
                    var am = pm.LockRead() as AlignmentModel;
                    if (am?.Alignment != null && am.Alignment == alignment)
                    {
                        targetModel = pm;
                        return true;
                    }
                }
                catch { }
                return false;
            });

            if (targetModel == null)
            {
                MessageDlg.Show("Модель не найдена.");
                return;
            }

            // Перебираем возможные TLC команды для открытия модели
            var cmds = new[] {
                "open_alignment", "open_model", "edit_alignment",
                "edit_model", "activate_model", "set_active_model"
            };
            bool executed = false;
            var lastError = "";
            foreach (var tlcCmd in cmds)
            {
                try
                {
                    ApplicationHost.Current.Plugins.Execute(tlcCmd, new object[] { targetModel });
                    executed = true;
                    break;
                }
                catch (Exception ex) { lastError = $"{tlcCmd}: {ex.Message}"; }
            }
            if (!executed)
                MessageDlg.Show("Не удалось открыть модель.\nПоследняя ошибка: " + lastError);
        }

        private Alignment GetAlignmentFromWrapper(object obj, System.Reflection.BindingFlags flags)
        {
            // AlignmentWrapper.Alignment → RoadAlignment
            // AlignmentWrapper.WrappedObject → RoadAlignment
            foreach (var propName in new[] { "Alignment", "WrappedObject" })
            {
                var val = GetPropValue(obj, propName, flags);
                if (val is Alignment a) return a;
            }

            // SurfacePointWrapper.Surface → ищем трассу у которой эта поверхность
            // Не можем напрямую — возвращаем null, пользователь должен кликнуть на объект трассы
            return null;
        }

        private object GetPropValue(object obj, string name, System.Reflection.BindingFlags flags)
        {
            if (obj == null) return null;
            for (var t = obj.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, flags);
                if (p != null)
                {
                    try { return p.GetValue(obj); }
                    catch { return null; }
                }
            }
            return null;
        }
    }
}
