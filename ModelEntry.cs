using Topomatic.ApplicationPlatform.Core;

namespace ModelDesk
{
    internal class ModelEntry
    {
        public IProjectModel Model;
        public string PathId;
        public string DisplayName;
        public string ModelType;
        public bool IsOpen;
        public bool IsActive;      // активная модель проекта (◉, подсветка строки)
        public bool IsChecked;
        public bool IsLocked;      // серверная модель без права "editor" → read-only (🔒)
        public string Group;
        public string UserStatus;
        public string Note;
        public string CachedDiskPath;
        public string CachedFileDate;
    }
}
