namespace BookProtoAPI.Controllers.TreeView.DTOs
{
    public class TreeNodeRequest
    {
        public int FirstVisibleRow { get; set; }
        public int RowsPerViewport { get; set; }
        public int FirstVisibleColumn { get; set; }
        public int ColumnsPerViewport { get; set; }
        public string SessionKey { get; set; } = null!;
        public int ParentID { get; set; }
        public int ID { get; set; }
        public int TreeLevel { get; set; }
        public int ChildCount { get; set; }
        public int SortID { get; set; }
        public DateOnly StageDate { get; set; }
    }
}
