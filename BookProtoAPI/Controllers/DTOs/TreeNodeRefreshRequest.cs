namespace BookProtoAPI.Controllers.DTOs
{
    public class TreeNodeRefreshRequest : TreeViewRequest
    {
        public RefreshedNode RefreshedNode { get; set; } = null!;
    }
}
