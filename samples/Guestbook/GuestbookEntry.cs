namespace BlazorCodeFirst.Samples.Guestbook;

public sealed record GuestbookEntry(int Id, string Name, string Message, DateTimeOffset CreatedAt);
