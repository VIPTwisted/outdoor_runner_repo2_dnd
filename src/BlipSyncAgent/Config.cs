namespace BlipSyncAgent;

public class Config {
    public string BlipUsername { get; }
    public string BlipPassword { get; }
    public string PostgresConnectionString { get; }
    public string BlipBaseUrl { get; }
    public string BlipOperatorSlug { get; }

    public Config() {
        BlipUsername              = Require("BLIP_USERNAME");
        BlipPassword              = Require("BLIP_PASSWORD");
        PostgresConnectionString  = Require("POSTGRES_CONNECTION_STRING");
        BlipBaseUrl               = Environment.GetEnvironmentVariable("BLIP_BASE_URL")       ?? "https://app.blipbillboards.com";
        BlipOperatorSlug          = Environment.GetEnvironmentVariable("BLIP_OPERATOR_SLUG")  ?? "k7b6gz";
    }

    private static string Require(string name) {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(v))
            throw new InvalidOperationException($"{name} not set — add it to GitHub Actions repository secrets");
        return v;
    }
}
