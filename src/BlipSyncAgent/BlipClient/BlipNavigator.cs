namespace BlipSyncAgent.BlipClient;

public class BlipNavigator {
    private readonly BlipSession _s;
    public BlipNavigator(BlipSession s) { _s = s; }

    public void GotoBoards()    => _s.GoTo($"/{_s.OperatorSlug}/signs");
    public void GotoCampaigns() => _s.GoTo($"/{_s.OperatorSlug}/campaigns");
    public void GotoAds()       => _s.GoTo($"/{_s.OperatorSlug}/ads");
    public void GotoDashboard() => _s.GoTo($"/{_s.OperatorSlug}/dashboard");
    // Extend with more nav targets as scraper grows.
}
