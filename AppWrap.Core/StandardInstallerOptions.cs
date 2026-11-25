namespace AppWrap.Core;

/// <summary>Optional completion actions for the standard installer only. Validation never performs I/O.</summary>
public sealed record StandardInstallerOptions(
    bool OfferLaunchAfterInstall = true,
    bool OfferStartWithWindows = false,
    bool OfferDeveloperWebsite = false,
    string? DeveloperWebsite = null)
{
    public static StandardInstallerOptions Normalize(StandardInstallerOptions? options)
    {
        options ??= new();
        return options with { DeveloperWebsite = options.OfferDeveloperWebsite ? ValidateWebsite(options.DeveloperWebsite) : null };
    }

    public static string ValidateWebsite(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 2048 ||
            value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '\'' or '"' or '`' or '\\') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new UserInputException("Use an absolute http:// or https:// website; omit spaces, quotes and sign-in details.");
        return uri.AbsoluteUri;
    }
}
