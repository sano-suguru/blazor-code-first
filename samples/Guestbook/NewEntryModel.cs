using System.ComponentModel.DataAnnotations;

namespace BlazorCodeFirst.Samples.Guestbook;

public sealed class NewEntryModel
{
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Message is required.")]
    public string Message { get; set; } = "";
}
