namespace SmartCord;

public sealed class PresencePayload
{
    public string ModeKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string Details { get; set; } = "";
    public string State { get; set; } = "";
    public string LargeImageKey { get; set; } = "";
    public string LargeImageText { get; set; } = "";
    public string SmallImageKey { get; set; } = "";
    public string SmallImageText { get; set; } = "";
    public string PrimaryButtonLabel { get; set; } = "View Project";
    public string PrimaryButtonUrl { get; set; } = "";
    public string SecondaryButtonLabel { get; set; } = "API";
    public string SecondaryButtonUrl { get; set; } = "";
}
