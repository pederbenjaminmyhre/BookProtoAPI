namespace BookProtoAPI.Controllers.TreeView.DTOs
{
    public class ExpandedNode
    {
        public int ParentID { get; set; }
        public int ID { get; set; }
        public int TreeLevel { get; set; }
        public int ChildCount { get; set; }
        public int SortID { get; set; }
        public DateTime StageDate { get; set; }
    }
}
