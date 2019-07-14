namespace TestApp
{
    public static class DisplayMetrics
    {
        // High resolution displays can require a lot of GPU and battery power to render.
        // High resolution phones, for example, may suffer from poor battery life if
        // games attempt to render at 60 frames per second at full fidelity.
        // The decision to render at full fidelity across all platforms and form factors
        // should be deliberate.
        public const bool SupportHighResolutions = false;

        // The default thresholds that define a "high resolution" display. If the thresholds
        // are exceeded and SupportHighResolutions is false, the dimensions will be scaled
        // by 50%.
        public const float DpiThreshold = 192.0f;       // 200% of standard desktop display.
        public const float WidthThreshold = 1920.0f;    // 1080p width.
        public const float HeightThreshold = 1080.0f;	// 1080p height.
    }
}
