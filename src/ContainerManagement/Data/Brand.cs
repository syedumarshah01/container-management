using System;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ContainerManagement.Data;

/// <summary>
/// The maker's mark. Bundled artwork (Assets\brand.png for light grounds, Assets\brand-light.png for the
/// navy rail) is what every install sees; files of the same names dropped in the data folder let one
/// machine carry its own with no rebuild. With nothing there, the plates typeset the wordmark in XAML -
/// a missing image should never be visible as a broken one.
/// </summary>
public static class Brand
{
    private static readonly Bitmap? _artwork;
    private static readonly Bitmap? _artworkOnDark;

    static Brand()
    {
        // Read once, when the shell is first touched. A broken or half-copied file counts as a missing
        // file here - the plates typeset the wordmark rather than showing an empty box.
        _artwork = Load("brand");
        _artworkOnDark = Load("brand-light") ?? _artwork;
    }

    /// <summary>The logo for white and paper grounds, or null when there is no artwork at all.</summary>
    public static Bitmap? Artwork => _artwork;

    /// <summary>
    /// The logo for the navy rail. Falls back to the ordinary one when only that was supplied, so an
    /// install with a single file still gets a picture down there instead of a hole - a light mark is
    /// the difference between a signature and an invisible smear on a dark ground.
    /// </summary>
    public static Bitmap? ArtworkOnDark => _artworkOnDark;

    private static Bitmap? Load(string stem)
    {
        var name = stem + ".png";
        return FromAsset("avares://ContainerManagement/Assets/" + name) ?? FromFile(Path.Combine(DbPaths.DirectoryPath, name));
    }

    private static Bitmap? FromAsset(string uri)
    {
        try
        {
            using var found = AssetLoader.Open(new Uri(uri));
            var copy = new MemoryStream();
            found.CopyTo(copy);
            copy.Position = 0;
            return new Bitmap(copy);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? FromFile(string path)
    {
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch
        {
            // A half-copied or unsupported image must not stop the books from opening.
            return null;
        }
    }
}
