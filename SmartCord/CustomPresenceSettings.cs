namespace SmartCord;

public sealed class CustomPresenceSettings
{
    public string Details { get; set; } = "Writing code";
    public string State { get; set; } = "I shouldve gone to bed";
    public string LargeImageKey { get; set; } = "vscode";
    public string LargeImageText { get; set; } = "VS Code";
    public string SmallImageKey { get; set; } = "terminal";
    public string SmallImageText { get; set; } = "SmartCord";
    public string Button1Label { get; set; } = "View Project";
    public string Button1Url { get; set; } = "";
    public string Button2Label { get; set; } = "API";
    public string Button2Url { get; set; } = "";

    public PresencePayload ToPayload(SmartCordSettings settings)
    {
        return new PresencePayload
        {
            ModeKey = "custom",
            Label = "Custom",
            Details = Details,
            State = State,
            LargeImageKey = LargeImageKey,
            LargeImageText = LargeImageText,
            SmallImageKey = SmallImageKey,
            SmallImageText = SmallImageText,
            PrimaryButtonLabel = Button1Label,
            PrimaryButtonUrl = Button1Url,
            SecondaryButtonLabel = Button2Label,
            SecondaryButtonUrl = string.IsNullOrWhiteSpace(Button2Url) ? settings.Api.ButtonUrl : Button2Url
        };
    }
}
