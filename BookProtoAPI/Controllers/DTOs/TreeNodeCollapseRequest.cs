namespace BookProtoAPI.Controllers.DTOs
{
    public class TreeNodeCollapseRequest : TreeViewRequest
    {
        public CollapsedNode CollapsedNode { get; set; } = null!;
    }
}
