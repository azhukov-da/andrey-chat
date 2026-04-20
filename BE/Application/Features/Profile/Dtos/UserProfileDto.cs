namespace Application.Features.Profile.Dtos;

public class UserProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
}
