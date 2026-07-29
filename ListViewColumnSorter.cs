using System.Collections;
using System.Windows.Forms;

namespace ModelDesk
{
    internal class ListViewColumnSorter : IComparer
    {
        private readonly CaseInsensitiveComparer _cmp = new CaseInsensitiveComparer();

        public int SortColumn { get; set; }
        public SortOrder Order { get; set; } = SortOrder.Ascending;

        public int Compare(object x, object y)
        {
            var a = (ListViewItem)x;
            var b = (ListViewItem)y;
            string ta = SortColumn < a.SubItems.Count ? a.SubItems[SortColumn].Text : string.Empty;
            string tb = SortColumn < b.SubItems.Count ? b.SubItems[SortColumn].Text : string.Empty;
            int r = _cmp.Compare(ta, tb);
            return Order == SortOrder.Ascending ? r : -r;
        }
    }
}
