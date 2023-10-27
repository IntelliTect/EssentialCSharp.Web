using System.Globalization;
using EssentialCSharp.Web.Middleware.Constants;

namespace EssentialCSharp.Web.Middleware;

/// <summary>
/// Exposes methods to build a policy.
/// </summary>
public class SecurityHeadersBuilder
{
    private readonly SecurityHeadersPolicy _Policy = new();

    //public SecurityHeadersBuilder() { }

    /// <summary>
    /// The number of seconds in one year
    /// </summary>
    public const int OneYearInSeconds = 60 * 60 * 24 * 365;

    /// <summary>
    /// Add default headers in accordance with most secure approach
    /// </summary>
    public SecurityHeadersBuilder AddDefaultSecurePolicy()
    {
        // Headers to Add: https://owasp.org/www-project-secure-headers/ci/headers_add.json
        // <add name="X-Frame-Options" value="DENY" />
        AddFrameOptionsDeny();
        // <add name="X-XSS-Protection" value="1; mode=block"/>
        AddXssProtectionBlock();
        // <add name="X-Content-Type-Options" value="nosniff" />
        AddContentTypeOptionsNoSniff();
        // <add name="Strict-Transport-Security" value="max-age=31536000; includeSubDomains" />
        AddStrictTransportSecurityMaxAgeIncludeSubDomains();
        // <add name="X-Permitted-Cross-Domain-Policies" value="master-only" />
        AddCustomHeader("X-Permitted-Cross-Domain-Policies", "master-only");
        // <add name="Referrer-Policy" value="no-referrer" />
        AddCustomHeader("Referrer-Policy", "no-referrer");
        // <add name="Permissions-Policy" value="accelerometer=(),ambient-light-sensor=(),autoplay=(),battery=(),camera=(),display-capture=(),document-domain=(),encrypted-media=(),fullscreen=(),gamepad=(),geolocation=(),gyroscope=(),layout-animations=(self),legacy-image-formats=(self),magnetometer=(),microphone=(),midi=(),oversized-images=(self),payment=(),picture-in-picture=(),publickey-credentials-get=(),speaker-selection=(),sync-xhr=(self),unoptimized-images=(self),unsized-media=(self),usb=(),screen-wake-lock=(),web-share=(),xr-spatial-tracking=()" />
        AddCustomHeader("Permissions-Policy", "accelerometer=(),ambient-light-sensor=(),autoplay=(),battery=(),camera=(),display-capture=(),document-domain=(),encrypted-media=(),fullscreen=(),gamepad=(),geolocation=(),gyroscope=(),layout-animations=(self),legacy-image-formats=(self),magnetometer=(),microphone=(),midi=(),oversized-images=(self),payment=(),picture-in-picture=(),publickey-credentials-get=(),speaker-selection=(),sync-xhr=(self),unoptimized-images=(self),unsized-media=(self),usb=(),screen-wake-lock=(),web-share=(),xr-spatial-tracking=()");

        // Headers to Remove: https://owasp.org/www-project-secure-headers/ci/headers_remove.json
        RemoveServerHeader();
        RemoveHeader("X-Powered-By");

        return this;
    }

    /// <summary>
    /// Add X-Frame-Options DENY to all requests.
    /// The page cannot be displayed in a frame, regardless of the site attempting to do so
    /// </summary>
    public SecurityHeadersBuilder AddFrameOptionsDeny()
    {
        _Policy.SetHeaders[FrameOptionsConstants.Header] = FrameOptionsConstants.Deny;
        return this;
    }

    /// <summary>
    /// Add X-Frame-Options SAMEORIGIN to all requests.
    /// The page can only be displayed in a frame on the same origin as the page itself.
    /// </summary>
    public SecurityHeadersBuilder AddFrameOptionsSameOrigin()
    {
        _Policy.SetHeaders[FrameOptionsConstants.Header] = FrameOptionsConstants.SameOrigin;
        return this;
    }

    /// <summary>
    /// Add X-Frame-Options ALLOW-FROM {uri} to all requests, where the uri is provided
    /// The page can only be displayed in a frame on the specified origin.
    /// </summary>
    /// <param name="uri">The uri of the origin in which the page may be displayed in a frame</param>
    public SecurityHeadersBuilder AddFrameOptionsSameOrigin(string uri)
    {
        _Policy.SetHeaders[FrameOptionsConstants.Header] = string.Format(CultureInfo.InvariantCulture, FrameOptionsConstants.AllowFromUri, uri);
        return this;
    }

    /// <summary>
    /// Add X-XSS-Protection 1 to all requests.
    /// Enables the XSS Protections
    /// </summary>
    public SecurityHeadersBuilder AddXssProtectionEnabled()
    {
        _Policy.SetHeaders[XssProtectionConstants.Header] = XssProtectionConstants.Enabled;
        return this;
    }

    /// <summary>
    /// Add X-XSS-Protection 0 to all requests.
    /// Disables the XSS Protections offered by the user-agent.
    /// </summary>
    public SecurityHeadersBuilder AddXssProtectionDisabled()
    {
        _Policy.SetHeaders[XssProtectionConstants.Header] = XssProtectionConstants.Disabled;
        return this;
    }

    /// <summary>
    /// Add X-XSS-Protection 1; mode=block to all requests.
    /// Enables XSS protections and instructs the user-agent to block the response in the event that script has been inserted from user input, instead of sanitizing.
    /// </summary>
    public SecurityHeadersBuilder AddXssProtectionBlock()
    {
        _Policy.SetHeaders[XssProtectionConstants.Header] = XssProtectionConstants.Block;
        return this;
    }

    /// <summary>
    /// Add X-XSS-Protection 1; report=http://site.com/report to all requests.
    /// A partially supported directive that tells the user-agent to report potential XSS attacks to a single URL. Data will be POST'd to the report URL in JSON format.
    /// </summary>
    public SecurityHeadersBuilder AddXssProtectionReport(string reportUrl)
    {
        _Policy.SetHeaders[XssProtectionConstants.Header] =
            string.Format(CultureInfo.InvariantCulture, XssProtectionConstants.Report, reportUrl);
        return this;
    }

    /// <summary>
    /// Add Strict-Transport-Security max-age=<see cref="maxAge"/> to all requests.
    /// Tells the user-agent to cache the domain in the STS list for the number of seconds provided.
    /// </summary>
    public SecurityHeadersBuilder AddStrictTransportSecurityMaxAge(int maxAge = OneYearInSeconds)
    {
        _Policy.SetHeaders[StrictTransportSecurityConstants.Header] =
            string.Format(CultureInfo.InvariantCulture, StrictTransportSecurityConstants.MaxAge, maxAge);
        return this;
    }

    /// <summary>
    /// Add Strict-Transport-Security max-age=<see cref="maxAge"/>; includeSubDomains to all requests.
    /// Tells the user-agent to cache the domain in the STS list for the number of seconds provided and include any sub-domains.
    /// </summary>
    public SecurityHeadersBuilder AddStrictTransportSecurityMaxAgeIncludeSubDomains(int maxAge = OneYearInSeconds)
    {
        _Policy.SetHeaders[StrictTransportSecurityConstants.Header] =
            string.Format(CultureInfo.InvariantCulture, StrictTransportSecurityConstants.MaxAgeIncludeSubdomains, maxAge);
        return this;
    }

    /// <summary>
    /// Add Strict-Transport-Security max-age=0 to all requests.
    /// Tells the user-agent to remove, or not cache the host in the STS cache
    /// </summary>
    public SecurityHeadersBuilder AddStrictTransportSecurityNoCache()
    {
        _Policy.SetHeaders[StrictTransportSecurityConstants.Header] =
            StrictTransportSecurityConstants.NoCache;
        return this;
    }

    /// <summary>
    /// Add X-Content-Type-Options nosniff to all requests.
    /// Can be set to protect against MIME type confusion attacks.
    /// </summary>
    public SecurityHeadersBuilder AddContentTypeOptionsNoSniff()
    {
        _Policy.SetHeaders[ContentTypeOptionsConstants.Header] = ContentTypeOptionsConstants.NoSniff;
        return this;
    }

    /// <summary>
    /// Removes the Server header from all responses
    /// </summary>
    public SecurityHeadersBuilder RemoveServerHeader()
    {
        _Policy.RemoveHeaders.Add(ServerConstants.Header);
        return this;
    }

    /// <summary>
    /// Adds a custom header to all requests
    /// </summary>
    /// <param name="header">The header name</param>
    /// <param name="value">The value for the header</param>
    /// <returns></returns>
    public SecurityHeadersBuilder AddCustomHeader(string header, string value)
    {
        if (string.IsNullOrEmpty(header))
        {
            throw new ArgumentNullException(nameof(header));
        }

        _Policy.SetHeaders[header] = value;
        return this;
    }

    /// <summary>
    /// Remove a header from all requests
    /// </summary>
    /// <param name="header">The to remove</param>
    /// <returns></returns>
    public SecurityHeadersBuilder RemoveHeader(string header)
    {
        if (string.IsNullOrEmpty(header))
        {
            throw new ArgumentNullException(nameof(header));
        }

        _Policy.RemoveHeaders.Add(header);
        return this;
    }

    /// <summary>
    /// Builds a new <see cref="SecurityHeadersPolicy"/> using the entries added.
    /// </summary>
    /// <returns>The constructed <see cref="SecurityHeadersPolicy"/>.</returns>
    public SecurityHeadersPolicy Build()
    {
        return _Policy;
    }
}
