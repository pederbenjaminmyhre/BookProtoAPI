namespace BookProtoAPI.Controllers.TreeView.DTOs
{
    public class TreeNodeRefreshRequest : TreeViewRequest
    {
        public RefreshedNode RefreshedNode { get; set; } = null!;
    }
}
