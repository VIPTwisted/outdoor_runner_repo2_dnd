namespace BlipSyncAgent.BlipClient;

public class BlipNavigator {
    private readonly BlipSession _s;
    public BlipNavigator(BlipSession s) { _s = s; }

    public void GotoDashboard()           => _s.GoTo($"/{_s.OperatorSlug}/dashboard");
    public void GotoPlantSigns()          => _s.GoTo($"/{_s.OperatorSlug}/signs/signs");
    public void GotoPlantPreview()        => _s.GoTo($"/{_s.OperatorSlug}/signs/preview");
    public void GotoPlantAds()            => _s.GoTo($"/{_s.OperatorSlug}/signs/ads");
    public void GotoImageVerification()   => _s.GoTo($"/{_s.OperatorSlug}/signs/image-verification");
    public void GotoAssignments()         => _s.GoTo($"/{_s.OperatorSlug}/signs/assignments");
    public void GotoAdvertisers()         => _s.GoTo($"/{_s.OperatorSlug}/signs/advertiser");
    public void GotoPlantReports()        => _s.GoTo($"/{_s.OperatorSlug}/signs/reports");
    public void GotoCampaigns()           => _s.GoTo($"/{_s.OperatorSlug}/campaigns");
    public void GotoAds()                 => _s.GoTo($"/{_s.OperatorSlug}/ads");
    public void GotoAdkomAvailability()   => _s.GoTo($"/{_s.OperatorSlug}/adkom/available");
    public void GotoAdkomHolds()          => _s.GoTo($"/{_s.OperatorSlug}/adkom/holds");
    public void GotoAdkomContracts()      => _s.GoTo($"/{_s.OperatorSlug}/adkom/contracts");
    public void GotoAdkomCreatives()      => _s.GoTo($"/{_s.OperatorSlug}/adkom/creatives");
    public void GotoAdkomPop()            => _s.GoTo($"/{_s.OperatorSlug}/adkom/pops");
    public void GotoMarketplace()         => _s.GoTo($"/{_s.OperatorSlug}/organizations/marketplace-analytics");
    public void GotoProgrammaticReports() => _s.GoTo($"/{_s.OperatorSlug}/organizations/programmatic/reports");
}
