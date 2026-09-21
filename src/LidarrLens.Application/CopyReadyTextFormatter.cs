using LidarrLens.Domain;

namespace LidarrLens.Application;

public static class CopyReadyTextFormatter
{
    public static string Format(CopyReadyRelease release) =>
        $"Release title: {release.Title}\n" +
        $"Type: {release.ReleaseGroupType}\n" +
        $"Date: {release.ReleaseDate}\n" +
        $"Country: {release.Country}\n" +
        $"Label: {release.Label}\n" +
        $"Catalog number: {release.CatalogNumber}\n" +
        $"Barcode: {release.Barcode}\n" +
        $"Format: {release.Format}\n" +
        $"Artwork URL: {release.ArtworkUrl ?? "(none)"}\n\n" +
        $"{string.Join("\n", release.Tracklist)}\n\n" +
        "Cover art: download and inspect the artwork, then upload it manually through the matching MusicBrainz release’s Cover Art tab.\n\n" +
        $"Edit note: {release.EditNote}";
}
