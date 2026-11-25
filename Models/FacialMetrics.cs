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
        /// Afrouxado de 0,17 para 0,19: câmeras/webcams entregam pálpebras fechadas com
        /// EAR mais alto que o ideal teórico, e o quadro deve virar "dormindo" na hora.
        /// </summary>
        public float EyeClosedEar { get; set; } = 0.19f;

        /// <summary>
        /// EAR máximo por olho para aceitar o fechamento. Impede que um aperto forte
        /// de um olho só (ou piscadela) vire "dormindo". Afrouxado junto com a média.
        /// </summary>
        public float EyeClosedMaxEar { get; set; } = 0.21f;

        /// <summary>
        /// Faixa "quase fechado": acima do fechamento estrito, mas ainda ambígua entre
        /// sono leve e aperto de dor. Nesta faixa, dentes cerrados desempatam para dor.
        /// </summary>
        public float EyeClosedSoftEar { get; set; } = 0.225f;

        /// <summary>EAR máximo por olho na faixa "quase fechado".</summary>
        public float EyeClosedSoftMaxEar { get; set; } = 0.245f;

        /// <summary>EAR abaixo do qual os olhos são considerados apertados (dor), mas não fechados.</summary>
        public float EyeNarrowEar { get; set; } = 0.23f;

        /// <summary>
        /// EAR médio máximo para "olhos apertados" na regra de dor.
        /// Mais tolerante que EyeNarrowEar: expressões de dor reais variam muito de olho para olho.
        /// </summary>
        public float PainEyeEar { get; set; } = 0.30f;

        /// <summary>
        /// EAR máximo aceito em qualquer um dos olhos na regra de dor. Permite assimetria
        /// natural (um olho mais aberto), mas ainda impede que um olho em repouso (~0,30+)
        /// passe como aperto de dor.
        /// </summary>
        public float PainEyeSideMaxEar { get; set; } = 0.34f;

        /// <summary>
        /// EAR mínimo para "olhos arregalados" (medo). Partia de 0,335, acima do repouso
        /// (~0,30–0,33) e pouco alcançável em webcam; com 0,31 captura olhos arregalados
        /// reais sem exigir abertura física impossível.
        /// </summary>
        public float WideEyeEar { get; set; } = 0.31f;

        /// <summary>
        /// Piso por olho para aceitar "olhos arregalados" (medo). O Face Mesh às vezes
        /// devolve um olho com ruído; o piso evita que só a média dispare.
        /// </summary>
        public float MinWideEyeSideEar { get; set; } = 0.275f;

        // ---- Rosto ----

        /// <summary>Largura mínima do rosto (px) para aceitar as métricas; abaixo disso o quadro é indeterminado.</summary>
        public float MinFaceWidthPx { get; set; } = 60f;

        // ---- Boca ----

        /// <summary>MAR mínimo para bocejo (abertura vertical / largura interna da boca).</summary>
        public float YawnMouthAspectRatio { get; set; } = 0.55f;

        /// <summary>Abertura da boca (px) / altura do rosto mínima para bocejo.</summary>
        public float YawnOpenRatio { get; set; } = 0.055f;

        /// <summary>
        /// Queda média dos cantos externos da boca em relação ao LÁBIO SUPERIOR (marco 13),
        /// normalizada pela largura da boca (61–291).
        ///
        /// Referência escolhida depois de comparar três opções em imagens rotuladas:
        /// o lábio superior interno sobe apenas levemente quando a mandíbula abre (o lábio
        /// inferior é que cede), então a métrica não dispara com a boca aberta; já o ponto
        /// médio 13/14 descia junto com o queixo e virava "sorriso" falso em caretas com
        /// boca aberta, e a comissura interna (78/308) acompanhava os cantos e anulava o sinal.
        /// Cantos acima do lábio superior (valor menor) => sorriso; abaixo (valor maior) => tristeza.
        /// </summary>
        public float FrownSagRatio { get; set; } = 0.07f;

        /// <summary>
        /// Queda dos cantos a partir da qual a boca é considerada "para baixo".
        /// Não é mais usada como exclusão da dor: caretas de dor reais também deprimem
        /// os cantos, e a prioridade dor > tristeza já resolve o conflito.
        /// </summary>
        public float PainMaxFrownSag { get; set; } = 0.03f;

        /// <summary>
        /// Faixa de dentes tão clara que dispensa a abertura mínima dos lábios no padrão
        /// de mandíbula travada (dentes à mostra com a boca quase fechada).
        /// </summary>
        public float StrongTeethRatio { get; set; } = 0.25f;

        /// <summary>
        /// Sag negativo (cantos acima do lábio superior) a partir do qual o rosto é "sorrindo";
        /// usado para não classificar sorriso com dentes como dor. Medido na mesma referência
        /// de <see cref="FrownSagRatio"/>.
        /// </summary>
        public float SmileCornerLiftRatio { get; set; } = 0.02f;

        /// <summary>FRAÇÃO mínima de pixels claros dentro da boca para considerar dentes visíveis.</summary>
        public float TeethPixelRatio { get; set; } = 0.04f;

        /// <summary>Mínimo absoluto de pixels de dente para evitar ruído em rostos pequenos.</summary>
        public int MinTeethPixels { get; set; } = 5;

        /// <summary>MAR máximo para considerar dentes cerrados (mandíbula travada = boca praticamente fechada).</summary>
        public float TeethMaxMouthAspectRatio { get; set; } = 0.22f;

        /// <summary>Fração mínima de dentes para o padrão "dentes cerrados" (mandíbula travada).</summary>
        public float MinClenchedTeethRatio { get; set; } = 0.07f;

        /// <summary>Mínimo de pixels de dente para o padrão "dentes cerrados" em webcam.</summary>
        public int MinClenchedTeethPixels { get; set; } = 18;

        /// <summary>
        /// Abertura vertical mínima dos lábios (px) para o padrão "dentes cerrados".
        /// Evita que lábios apenas entreabertos (faixa fina de dentes) virem dor.
        /// </summary>
        public float MinClenchedOpenPx { get; set; } = 1.5f;

        /// <summary>
        /// Aperto ocular mais forte exigido nos padrões de dentes cerrados: olho em
        /// repouso fica em ~0,28–0,34, então 0,28 exige um aperto inequívoco.
        /// </summary>
        public float PainClenchedEyeEar { get; set; } = 0.28f;

        /// <summary>MAR mínimo para "careta de dor" com boca bem aberta mostrando dentes.</summary>
        public float GrimaceMinMouthAspectRatio { get; set; } = 0.30f;

        /// <summary>Fração de dentes a partir da qual, com boca aberta, o rosto é tratado como sorriso largo (não dor).</summary>
        public float GrinTeethRatio { get; set; } = 0.40f;

        /// <summary>MAR a partir do qual a exclusão de sorriso largo se aplica.</summary>
        public float GrinMinMouthAspectRatio { get; set; } = 0.15f;

        // ---- Sobrancelhas ----

        /// <summary>
        /// Elevação da sobrancelha interna acima da linha dos cantos dos olhos,
        /// normalizada pela distância interocular. Repouso observado na webcam ~0,30;
        /// limiar a 0,29 captura sobrancelhas erguidas sem confundir com repouso.
        /// </summary>
        public float BrowRaiseRatio { get; set; } = 0.29f;

        /// <summary>
        /// Inclinação das sobrancelhas: quanto o ponto interno (glabela) está ACIMA da
        /// ponta externa, normalizado pela distância interocular. Positivo = sobrancelhas
        /// "tristes" (AU1, canto interno levantado); negativo = sobrancelhas franzidas
        /// contra a glabela (AU4, típico de dor).
        /// </summary>
        public float SadBrowSlantRatio { get; set; } = 0.018f;

        /// <summary>
        /// Elevação da sobrancelha interna que também caracteriza tristeza (AU1),
        /// como alternativa à inclinação em rostos com cauda de sobrancelha curta.
        /// Exige elevação real (0,32): o repouso da webcam gira em torno de 0,24–0,30,
        /// então este caminho não deve disparar sozinho em um rosto neutro.
        /// </summary>
        public float SadBrowRaiseRatio { get; set; } = 0.32f;

        /// <summary>
        /// Queda dos cantos da boca em relação ao lábio superior (13) que caracteriza
        /// tristeza (AU15). Convive com <see cref="FrownSagRatio"/>, mais restritivo.
        /// </summary>
        public float SadMouthSagRatio { get; set; } = 0.010f;

        /// <summary>
        /// Queda dos cantos em relação ao centro da linha da boca; alternativa de
        /// evidência para tristeza quando o lábio superior está pouco destacado.
        /// </summary>
        public float SadMouthSagSeamRatio { get; set; } = 0.007f;

        /// <summary>
        /// Queda dos cantos em relação à comissura interna (78/308). Terceira referência
        /// de curvatura da boca: escala neutra em ~0 e mais estável que o lábio superior
        /// em bocas finas/fechadas ou projetadas ("beiço").
        /// </summary>
        public float SadMouthSagInnerRatio { get; set; } = 0.010f;

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
        public float MouthWidthRatio { get; init; }
        public float MouthCornerSagRatio { get; init; }
        public float MouthCornerSagLeft { get; init; }
        public float MouthCornerSagRight { get; init; }

        /// <summary>Diagnóstico/calibração: queda dos cantos referenciada ao lábio superior (13).</summary>
        public float MouthCornerSagUpperLip { get; init; }

        /// <summary>Diagnóstico/calibração: queda dos cantos referenciada à comissura interna (78/308).</summary>
        public float MouthCornerSagInner { get; init; }

        public float BrowRaiseRatio { get; init; }

        /// <summary>Inclinação das sobrancelhas (interno - externo, x IOD); positivo = AU1/tristeza.</summary>
        public float BrowSlantRatio { get; init; }

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

        /// <summary>
        /// Aperto ocular de dor em vigília: média <= limiar de aperto e nenhum olho
        /// acima do limite de assimetria. Não usa "ambos os olhos abaixo do limiar",
        /// que falhava em rostos com um olho naturalmente mais aberto.
        /// </summary>
        public bool EyesPainSquinted { get; init; }

        /// <summary>Aperto ocular forte (mandíbula travada): o olho MAIS ABERTO ainda está apertado.</summary>
        public bool EyesTightSqueezed { get; init; }

        /// <summary>Maior EAR entre os dois olhos (diagnóstico de assimetria).</summary>
        public float EyeSquintMaxEar { get; init; }

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
