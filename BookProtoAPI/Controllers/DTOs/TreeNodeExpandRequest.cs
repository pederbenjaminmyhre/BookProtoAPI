namespace BookProtoAPI.Controllers.DTOs
{
    public class TreeNodeExpandRequest : TreeViewRequest
    {
        public ExpandedNode ExpandedNode { get; set; } = null!;
    }
}
