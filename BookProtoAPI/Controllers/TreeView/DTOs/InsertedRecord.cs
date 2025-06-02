namespace BookProtoAPI.Controllers.TreeView.DTOs
{
    public class InsertedRecord
    {
        public int ParentID { get; set; }
        public bool HasChildren { get; set; }
        public int ChildCount { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateOnly StageDate { get; set; }

        public string SessionKey { get; set; } = null!;
    }
}
