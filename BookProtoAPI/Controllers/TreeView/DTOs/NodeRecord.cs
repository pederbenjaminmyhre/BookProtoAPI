namespace BookProtoAPI.Controllers.TreeView.DTOs
{
    public class NodeRecord
    {
        public int? id { get; set; }
        public int? parentId { get; set; }
        public bool hasChildren { get; set; }
        public int childCount { get; set; }
        public string name { get; set; } = string.Empty;
        public DateOnly stageDate { get; set; }
        public int? sortId { get; set; }
    }
}
