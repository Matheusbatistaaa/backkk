using System.Collections.Generic;

namespace PerceptAI.API.Models
{
    /// <summary>
    /// Limiares do motor de regras. Todos os valores ficam aqui para que a
    /// calibração seja auditável e aplicada de forma idêntica a todas as classes.
    /// </summary>
    public sealed class EmotionRuleOptions
    {
        /// <summary>EAR abaixo do qual os olhos são considerados fechados (dormindo).</summary>
        public float EyeClosedEar { get; init; } = 0.17f;

        /// <summary>EAR abaixo do qual os olhos estão apertados (dor) mas ainda abertos.</summary>
        public float EyeNarrowEar { get; init; } = 0.23f;

        /// <summary>MAR máximo para que olhos fechados sejam lidos como sono (boca aberta indica outra classe).</summary>
        public float SleepMaxMouthAspectRatio { get; init; } = 0.20f;

        /// <summary>MAR mínimo para bocejo (abertura vertical / largura interna da boca).</summary>
        public float YawnMouthAspectRatio { get; init; } = 0.55f;

        /// <summary>Abertura da boca (px) / altura do rosto mínima para bocejo.</summary>
        public float YawnOpenRatio { get; init; } = 0.055f;

        /// <summary>Queda dos cantos da boca (normalizada pela largura) para tristeza.</summary>
        public float FrownSagRatio { get; init; } = 0.04f;

        /// <summary>Elevação da sobrancelha interna em relação ao olho, normalizada pela distância interocular.</summary>
        public float BrowRaiseRatio { get; init; } = 0.22f;

        /// <summary>Fração de pixels claros dentro da boca para considerar dentes visíveis.</summary>
        public float TeethPixelRatio { get; init; } = 0.12f;

        /// <summary>Mínimo absoluto de pixels de dente para evitar ruído em rostos pequenos.</summary>
        public int MinTeethPixels { get; init; } = 20;

        /// <summary>MAR máximo para considerar dentes cerrados (boca aberta demais = outra classe).</summary>
        public float TeethMaxMouthAspectRatio { get; init; } = 0.50f;

        /// <summary>Pontos da mão dentro da região da boca para caracterizar "mão na boca".</summary>
        public int MinHandPointsInMouthRegion { get; init; } = 3;

        /// <summary>Meia-largura da região da boca, em múltiplos da largura da boca.</summary>
        public float HandRegionWidthFactor { get; init; } = 0.90f;

        /// <summary>Meia-altura da região da boca, em múltiplos da largura da boca.</summary>
        public float HandRegionHeightFactor { get; init; } = 1.10f;

        /// <summary>Score mínimo do Face Mesh para aceitar os marcos.</summary>
        public float MinFaceMeshScore { get; init; } = 0.50f;

        /// <summary>Score mínimo da detecção de rosto e de palma.</summary>
        public float MinDetectionScore { get; init; } = 0.50f;
    }

    /// <summary>Métricas geométricas e fotométricas extraídas de um único quadro.</summary>
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
