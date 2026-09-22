namespace LidarrLens.Web;

public sealed record MusicBrainzTaskViewOptions(string Mode)
{
    public bool UseIframe => string.Equals(Mode, "iframe", StringComparison.OrdinalIgnoreCase);
}
