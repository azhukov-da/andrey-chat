namespace Application.Features.Sessions.Dtos;

public class UserSessionDto
{
    public Guid Id { get; set; }
    public string? DeviceInfo { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public bool IsCurrent { get; set; }
}
