namespace BookProtoAPI.Controllers.DTOs
{
    public class TreeNodeResult
    {
        public int ID { get; set; }
        public int ParentID { get; set; }
        public int TreeLevel { get; set; }
        public int SortID { get; set; }
        public bool HasChildren { get; set; }
        public int ChildCount { get; set; }
        public DateTime StageDate { get; set; }
        public bool IsExpanded { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
