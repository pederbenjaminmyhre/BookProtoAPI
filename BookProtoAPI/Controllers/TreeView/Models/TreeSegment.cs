namespace BookProtoAPI.Controllers.TreeView.Models
{
    public class TreeSegment
    {
        public int SegmentID { get; set; }
        public int ParentSegmentID { get; set; }
        public int SegmentPosition { get; set; }
        public int ParentID { get; set; }
        public int TreeLevel { get; set; }
        public DateOnly StageDate { get; set; }
        public int RecordCount { get; set; }
        public int FirstTreeRow { get; set; }
        public int LastTreeRow { get; set; }
        public int FirstSortID { get; set; }
        public int LastSortID { get; set; }
    }
}
