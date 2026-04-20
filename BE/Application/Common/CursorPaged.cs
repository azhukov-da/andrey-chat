namespace Application.Common;

public class CursorPaged<T>
{
    public List<T> Items { get; set; } = new();
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }
}
