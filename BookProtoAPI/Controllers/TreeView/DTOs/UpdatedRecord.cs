namespace BookProtoAPI.Controllers.TreeView.DTOs
{
    public class UpdatedRecord
    {
        public int ID { get; set; }
        public int ParentID { get; set; }
        public bool HasChildren { get; set; }
        public int ChildCount { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime StageDate { get; set; }
    }
}
