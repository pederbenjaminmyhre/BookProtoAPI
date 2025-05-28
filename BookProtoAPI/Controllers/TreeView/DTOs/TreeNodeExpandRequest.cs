namespace BookProtoAPI.Controllers.TreeView.DTOs
{
    public class TreeNodeExpandRequest : TreeViewRequest
    {
        public ExpandedNode ExpandedNode { get; set; } = null!;
    }
}
