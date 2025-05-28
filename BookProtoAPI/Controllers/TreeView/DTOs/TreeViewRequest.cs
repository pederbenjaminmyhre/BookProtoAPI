namespace BookProtoAPI.Controllers.TreeView.DTOs
{
    public class TreeViewRequest
    {
        public int RowsPerViewport { get; set; }
        public int ColumnsPerViewport { get; set; }
        public int FirstVisibleRow { get; set; }
        public int FirstVisibleColumn { get; set; }
        public int RootID { get; set; }
        public string SessionKey { get; set; } = null!; // required
    }
}
