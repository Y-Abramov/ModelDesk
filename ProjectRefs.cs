using System.Collections;
using Topomatic.FoundationClasses;

namespace ModelDesk
{
    // Доступ к References корневой модели проекта ЧЕРЕЗ РЕФЛЕКСИЮ - ради мультиверсионности.
    // В SDK 16.0.62.12 rootModel.LockRead() -> конкретный класс RoburProjectModel с
    // типизированным свойством References; в 16.0.60.11 (Genplan) этого класса-имени в SDK
    // нет, поэтому прямой каст `as RoburProjectModel` не компилируется под 60.11 и роняет
    // загрузку ВСЕЙ сборки на Genplan (strong-name). LockRead() объявлен как object в обеих
    // версиях, поэтому доступ к References/Uri через рефлексию собирается под любой SDK.
    //
    // Рантайм: на 62.12 рефлексия находит те же члены -> поведение идентично типизированному.
    // На 60.11, если рантайм-объект несёт свойство References - работает; если нет (старый
    // API без этого понятия) - Enumerate/GetUri вернут null, вызывающий тихо пропускает
    // репойнт ссылок (файл всё равно переименован/перемещён - мягкая деградация, не краш).
    internal static class ProjectRefs
    {
        // Сырое значение свойства References (для доступа как IList / .Add) или null.
        internal static object Raw(object rootData)
        {
            return rootData == null
                ? null
                : rootData.GetType().GetProperty("References")?.GetValue(rootData);
        }

        // References как IEnumerable для foreach, или null если свойства нет.
        internal static IEnumerable Enumerate(object rootData)
        {
            return Raw(rootData) as IEnumerable;
        }

        // reference.Uri (get) или null.
        internal static URI GetUri(object reference)
        {
            return reference?.GetType().GetProperty("Uri")?.GetValue(reference) as URI;
        }

        // reference.Uri = value; false если свойства нет/не записываемо.
        internal static bool SetUri(object reference, URI value)
        {
            var p = reference?.GetType().GetProperty("Uri");
            if (p == null || !p.CanWrite) return false;
            p.SetValue(reference, value);
            return true;
        }
    }
}
