using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Support.UI;

namespace BlipSyncAgent.BlipClient;

public class BlipSession : IDisposable {
    private readonly Config _cfg;
    private readonly IWebDriver _driver;
    private string? _authenticatedBaseUrl;
    public IWebDriver Driver => _driver;
    public string BaseUrl => _cfg.BlipBaseUrl.TrimEnd('/');
    public string OperatorSlug => _cfg.BlipOperatorSlug;

    public BlipSession(Config cfg) {
        _cfg = cfg;
        var options = new EdgeOptions();
        options.AddArgument("--headless=new");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--window-size=1400,900");
        options.AddArgument("--disable-blink-features=AutomationControlled");
        _driver = new EdgeDriver(options);
        _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);
        _driver.Manage().Timeouts().PageLoad     = TimeSpan.FromSeconds(60);
    }

    public async Task LoginAsync() {
        Console.WriteLine($"[BlipSession] navigating to {BaseUrl} for login");
        _driver.Navigate().GoToUrl(BaseUrl);
        await Task.Delay(3000);

        // If we're already authenticated (cookies on dedicated runner won't carry between runs,
        // so this is informational only), URL will not contain /login.
        if (!_driver.Url.Contains("/login", StringComparison.OrdinalIgnoreCase)) {
            Console.WriteLine("[BlipSession] not on login page — already authenticated?");
            return;
        }

        var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));
        var userInput = wait.Until(d => d.FindElement(By.CssSelector("input[type='email'], input[name='email'], input#email")));
        var passInput = _driver.FindElement(By.CssSelector("input[type='password'], input[name='password'], input#password"));

        userInput.Clear(); userInput.SendKeys(_cfg.BlipUsername);
        passInput.Clear(); passInput.SendKeys(_cfg.BlipPassword);

        var submit = _driver.FindElement(By.CssSelector("button[type='submit'], button[name='login']"));
        submit.Click();

        // Wait for the URL to leave /login. If 2FA is enabled, this will time out — see README.
        try {
            wait.Until(d => !d.Url.Contains("/login", StringComparison.OrdinalIgnoreCase));
        } catch (WebDriverTimeoutException) {
            Console.Error.WriteLine("[BlipSession] login wait timed out — likely 2FA or wrong creds. Title=" + _driver.Title);
            throw;
        }

        _authenticatedBaseUrl = ResolveAuthenticatedBaseUrl(_driver.Url);
        Console.WriteLine("[BlipSession] login OK url=" + _driver.Url);
        Console.WriteLine("[BlipSession] authenticated base=" + _authenticatedBaseUrl);
    }

    public void GoTo(string path) {
        var url = path.StartsWith("http") ? path : (_authenticatedBaseUrl ?? BaseUrl) + path;
        Console.WriteLine("[BlipSession] GET " + url);
        _driver.Navigate().GoToUrl(url);
    }

    private string ResolveAuthenticatedBaseUrl(string currentUrl) {
        if (Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri)) {
            return $"{uri.Scheme}://{uri.Host}";
        }

        return BaseUrl;
    }

    public void Dispose() {
        try { _driver.Quit(); } catch { }
        _driver.Dispose();
    }
}
