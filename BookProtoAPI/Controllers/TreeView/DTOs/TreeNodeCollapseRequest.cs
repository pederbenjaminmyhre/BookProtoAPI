namespace BookProtoAPI.Controllers.TreeView.DTOs
{
    public class TreeNodeCollapseRequest : TreeViewRequest
    {
        public CollapsedNode CollapsedNode { get; set; } = null!;
    }
}
