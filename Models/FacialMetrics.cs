using System.Collections.Generic;

namespace PerceptAI.API.Models
{
    /// <summary>
    /// Limiares e janelas geométricas do motor de regras.
    /// Todos os valores ficam em um único lugar para que o ajuste seja auditável
    /// e aplicado de forma idêntica a todas as emoções.
    /// </summary>
    public sealed class EmotionRuleOptions
    {
        // ---- Olhos (EAR = razão de aspecto do olho) ----

        /// <summary>
        /// EAR médio abaixo do qual os olhos são considerados realmente fechados (dormindo).
        /// Deliberadamente abaixo da faixa de "olhos apertados" da dor (~0,17–0,31):
        /// aperto de dor não é sono.
        /// </summary>
        public float EyeClosedEar { get; set; } = 0.17f;

        /// <summary>
        /// EAR máximo por olho para aceitar o fechamento. Impede que um aperto forte
        /// de um olho só (ou piscadela) vire "dormindo".
        /// </summary>
        public float EyeClosedMaxEar { get; set; } = 0.195f;

        /// <summary>
        /// Faixa "quase fechado": acima do fechamento estrito, mas ainda ambígua entre
        /// sono leve e aperto de dor. Nesta faixa, dentes cerrados desempatam para dor.
        /// </summary>
        public float EyeClosedSoftEar { get; set; } = 0.21f;

        /// <summary>EAR máximo por olho na faixa "quase fechado".</summary>
        public float EyeClosedSoftMaxEar { get; set; } = 0.235f;

        /// <summary>EAR abaixo do qual os olhos são considerados apertados (dor), mas não fechados.</summary>
        public float EyeNarrowEar { get; set; } = 0.23f;

        /// <summary>
        /// EAR máximo (nos dois olhos) para "olhos apertados" na regra de dor.
        /// Mais tolerante que EyeNarrowEar: expressões de dor reais variam muito de olho para olho.
        /// </summary>
        public float PainEyeEar { get; set; } = 0.31f;

        /// <summary>
        /// EAR mínimo nos DOIS olhos para "olhos arregalados" (medo). Acima do repouso
        /// medido na webcam (~0,28–0,33), então exige abertura real dos olhos.
        /// </summary>
        public float WideEyeEar { get; set; } = 0.335f;

        /// <summary>
        /// Piso por olho para aceitar "olhos arregalados" (medo). O Face Mesh às vezes
        /// devolve um olho com ruído; o piso evita que só a média dispare.
        /// </summary>
        public float MinWideEyeSideEar { get; set; } = 0.30f;

        // ---- Rosto ----

        /// <summary>Largura mínima do rosto (px) para aceitar as métricas; abaixo disso o quadro é indeterminado.</summary>
        public float MinFaceWidthPx { get; set; } = 60f;

        // ---- Boca ----

        /// <summary>MAR mínimo para bocejo (abertura vertical / largura interna da boca).</summary>
        public float YawnMouthAspectRatio { get; set; } = 0.55f;

        /// <summary>Abertura da boca (px) / altura do rosto mínima para bocejo.</summary>
        public float YawnOpenRatio { get; set; } = 0.055f;

        /// <summary>
        /// Queda dos cantos externos em relação à comissura interna da boca, normalizada
        /// pela largura. A comissura interna não se move ao abrir a boca, então o valor
        /// não é afetado por bocejo/grito (ao contrário de usar o lábio superior).
        /// </summary>
        public float FrownSagRatio { get; set; } = 0.035f;

        /// <summary>
        /// Queda dos cantos a partir da qual a boca é considerada "para baixo" também na
        /// triagem da dor. Dor não puxa os cantos para baixo; valores iguais ou maiores
        /// bloqueiam a regra de dor e deixam o quadro para a tristeza.
        /// </summary>
        public float PainMaxFrownSag { get; set; } = 0.02f;

        /// <summary>
        /// Sag negativo (cantos acima da comissura) a partir do qual o rosto é "sorrindo";
        /// usado para não classificar sorriso com dentes como dor.
        /// </summary>
        public float SmileCornerLiftRatio { get; set; } = 0.012f;

        /// <summary>Fração de pixels claros dentro da boca para considerar dentes visíveis.</summary>
        public float TeethPixelRatio { get; set; } = 0.05f;

        /// <summary>Mínimo absoluto de pixels de dente para evitar ruído em rostos pequenos.</summary>
        public int MinTeethPixels { get; set; } = 6;

        /// <summary>MAR máximo para considerar dentes cerrados (mandíbula travada = boca praticamente fechada).</summary>
        public float TeethMaxMouthAspectRatio { get; set; } = 0.15f;

        /// <summary>Fração mínima de dentes para o padrão "dentes cerrados" (mandíbula travada).</summary>
        public float MinClenchedTeethRatio { get; set; } = 0.12f;

        /// <summary>Mínimo de pixels de dente para o padrão "dentes cerrados" em webcam.</summary>
        public int MinClenchedTeethPixels { get; set; } = 40;

        /// <summary>
        /// Abertura vertical mínima dos lábios (px) para o padrão "dentes cerrados".
        /// Evita que lábios apenas entreabertos (faixa fina de dentes) virem dor.
        /// </summary>
        public float MinClenchedOpenPx { get; set; } = 2.5f;

        /// <summary>
        /// Aperto ocular mais forte exigido nos padrões de dentes cerrados: olho em
        /// repouso fica em ~0,28–0,34, então 0,25 exige um aperto inequívoco.
        /// </summary>
        public float PainClenchedEyeEar { get; set; } = 0.25f;

        /// <summary>MAR mínimo para "careta de dor" com boca bem aberta mostrando dentes.</summary>
        public float GrimaceMinMouthAspectRatio { get; set; } = 0.35f;

        /// <summary>Fração de dentes a partir da qual, com boca aberta, o rosto é tratado como sorriso largo (não dor).</summary>
        public float GrinTeethRatio { get; set; } = 0.40f;

        /// <summary>MAR a partir do qual a exclusão de sorriso largo se aplica.</summary>
        public float GrinMinMouthAspectRatio { get; set; } = 0.15f;

        // ---- Sobrancelhas ----

        /// <summary>
        /// Elevação da sobrancelha interna acima da linha dos cantos dos olhos,
        /// normalizada pela distância interocular.
        /// Repouso observado na webcam fica em ~0,30; como o medo agora exige também
        /// olhos arregalados, este limiar pode partir do repouso sem gerar falso positivo.
        /// </summary>
        public float BrowRaiseRatio { get; set; } = 0.31f;

        // ---- Mãos ----

        /// <summary>Pontos da mão dentro da região da boca para caracterizar "mão na boca".</summary>
        public int MinHandPointsInMouthRegion { get; set; } = 3;

        /// <summary>Meia-largura da região da boca, em múltiplos da largura da boca.</summary>
        public float HandRegionWidthFactor { get; set; } = 0.90f;

        /// <summary>Meia-altura da região da boca, em múltiplos da largura da boca.</summary>
        public float HandRegionHeightFactor { get; set; } = 1.10f;

        /// <summary>Distância máxima centróide-mão -> boca (x distância interocular) para validar o gesto.</summary>
        public float MaxHandMouthDistanceRatio { get; set; } = 1.80f;

        // ---- Detectores ----

        /// <summary>Score mínimo do Face Mesh para aceitar os marcos.</summary>
        public float MinFaceMeshScore { get; set; } = 0.50f;

        /// <summary>Score mínimo da detecção de rosto e de palma.</summary>
        public float MinDetectionScore { get; set; } = 0.50f;
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
        public float MouthOpeningPx { get; init; }
        public float MouthWidth { get; init; }
        public float MouthCornerSagRatio { get; init; }

        public float BrowRaiseRatio { get; init; }

        public float TeethRatio { get; init; }
        public int TeethPixels { get; init; }
        public int MouthRegionPixels { get; init; }

        public int HandPointsInMouthRegion { get; init; }
        public float HandMouthDistanceRatio { get; init; }
        public bool HandDetected { get; init; }

        // ---- Estados derivados (avaliados por olho, com exigência de simetria) ----
        public bool RightEyeClosed { get; init; }
        public bool LeftEyeClosed { get; init; }

        /// <summary>Ambos os olhos fechados. Um piscar unilateral NÃO conta como dormindo.</summary>
        public bool EyesClosed { get; init; }

        /// <summary>Olhos na faixa ambígua entre sono leve e aperto (EAR ~0,17–0,21).</summary>
        public bool EyesAlmostClosed { get; init; }

        /// <summary>Ambos os olhos apertados (abaixo do limiar de aperto e acima do de fechamento).</summary>
        public bool EyesNarrowed { get; init; }

        /// <summary>Ambos os olhos abertos (acima do limiar de aperto) e nenhum fechado.</summary>
        public bool EyesOpen { get; init; }

        /// <summary>Ambos os olhos acima do limiar de arregalar (medo), sem fechamento.</summary>
        public bool EyesWide { get; init; }

        public bool Yawning { get; init; }
        public bool Frowning { get; init; }
        public bool Smiling { get; init; }
        public bool BrowsRaised { get; init; }
        public bool TeethVisible { get; init; }
        public bool TeethClenched { get; init; }
        public bool HandOnMouth { get; init; }

        public IReadOnlyList<string> Notes { get; init; } = System.Array.Empty<string>();
    }
}
