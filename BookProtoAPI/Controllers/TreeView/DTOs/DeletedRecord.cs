namespace BookProtoAPI.Controllers.TreeView.DTOs
{
    public class DeletedRecord
    {
        public int ID { get; set; }
        public DateOnly StageDate { get; set; }
        public string SessionKey { get; set; } = null!;
    }
}
