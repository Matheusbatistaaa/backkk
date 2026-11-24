using System.Collections.Generic;

namespace PerceptAI.API.Models
{
    
    public sealed class EmotionRuleOptions
    {
        
        public float EyeClosedEar { get; init; } = 0.17f;

        public float EyeNarrowEar { get; init; } = 0.23f;

        public float SleepMaxMouthAspectRatio { get; init; } = 0.20f;

        public float YawnMouthAspectRatio { get; init; } = 0.55f;

        public float YawnOpenRatio { get; init; } = 0.055f;

        public float FrownSagRatio { get; init; } = 0.04f;

        public float BrowRaiseRatio { get; init; } = 0.22f;

        public float TeethPixelRatio { get; init; } = 0.12f;

        public int MinTeethPixels { get; init; } = 20;

        public float TeethMaxMouthAspectRatio { get; init; } = 0.50f;

        public int MinHandPointsInMouthRegion { get; init; } = 3;

        public float HandRegionWidthFactor { get; init; } = 0.90f;

        public float HandRegionHeightFactor { get; init; } = 1.10f;

        public float MinFaceMeshScore { get; init; } = 0.50f;

        public float MinDetectionScore { get; init; } = 0.50f;
    }

    public sealed class FacialMetrics
    {
        public float InterocularDistance { get; init; }
        public float FaceWidth { get; init; }
        public float FaceHeight { get; init; }

        public float EyeAspectRight { get; init; }
        public float EyeAspectLeft { get; init; }
        public float EyeAspectRatio { get; init; }

        public float MouthAspectRatio { get; init; }
        public float MouthOpenRatio { get; init; }
        public float MouthWidth { get; init; }
        public float MouthCornerSagRatio { get; init; }

        public float BrowRaiseRatio { get; init; }

        public float TeethRatio { get; init; }
        public int TeethPixels { get; init; }
        public int MouthRegionPixels { get; init; }

        public int HandPointsInMouthRegion { get; init; }
        public float HandMouthDistanceRatio { get; init; }
        public bool HandDetected { get; init; }

        public bool EyesClosed { get; init; }
        public bool EyesNarrowed { get; init; }
        public bool EyesOpen { get; init; }
        public bool Yawning { get; init; }
        public bool Frowning { get; init; }
        public bool BrowsRaised { get; init; }
        public bool TeethVisible { get; init; }
        public bool TeethClenched { get; init; }
        public bool HandOnMouth { get; init; }

        public IReadOnlyList<string> Notes { get; init; } = System.Array.Empty<string>();
    }
}
