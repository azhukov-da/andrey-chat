namespace Domain.Entities;

public class UserBlock
{
    public string BlockerId { get; set; } = string.Empty;
    public ApplicationUser Blocker { get; set; } = null!;
    
    public string BlockedId { get; set; } = string.Empty;
    public ApplicationUser Blocked { get; set; } = null!;
    
    public DateTime CreatedAt { get; set; }
}
