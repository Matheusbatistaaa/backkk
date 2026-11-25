using System;
using System.Collections.Generic;
using System.Linq;
using PerceptAI.API.Models;

namespace PerceptAI.API.Services
{
    /// <summary>
    /// Motor de regras determinístico orientado a dados.
    ///
    /// Todas as emoções são descritas como <see cref="RuleSpec"/> (olhos exigidos,
    /// critérios obrigatórios, exclusões e sinais de confiança) e passam pelo MESMO
    /// avaliador (<see cref="EvaluateRule"/>). Não existe caminho especial por classe.
    ///
    /// Ordem de prioridade quando duas regras casam ao mesmo tempo:
    ///   dor > enjoo > medo > tristeza > sono
    /// Justificativa clínica: dor exige dois sinais simultâneos e é a condição mais
    /// urgente; enjoo exige o gesto físico (mão na boca); as demais têm evidência única.
    ///
    /// Estado de vigília tem precedência sobre o estado principal: olhos fechados = dormindo.
    /// Dormindo NÃO anula mais a leitura emocional: a mesma malha é reavaliada sem o
    /// portão de vigília e a emoção resultante é devolvida como secundária.
    /// </summary>
    public sealed class EmotionRuleEngine
    {
        private static readonly string[] PriorityOrder = { "dor", "enjoo", "medo", "tristeza", "sono" };

        private enum RuleContext
        {
            Awake,
            SleepSecondary
        }

        private readonly EmotionRuleOptions _o;

        public EmotionRuleEngine(EmotionRuleOptions options) => _o = options;

        public EmotionDecision Evaluate(FacialMetrics m, ExpressionBaseline? baseline = null)
        {
            var awakeRules = BuildRules(RuleContext.Awake, baseline)
                .Select(spec => EvaluateRule(spec, m))
                .ToList();

            // Confiança de sono cresce conforme o EAR médio cai abaixo do limiar de fechamento.
            float sleepConfidence = Clamp01(0.5f + 0.5f * (_o.EyeClosedEar - m.EyeAspectRatio)
                                                   / MathF.Max(_o.EyeClosedEar, 0.01f));

            // Dentes à mostra com olhos apertados/fechados caracterizam careta de dor mesmo
            // com os olhos totalmente cerrados (aperto forte), que de outra forma viraria sono.
            bool strongPainTeeth = m.TeethRatio >= _o.StrongTeethRatio
                                   && m.TeethPixels >= _o.MinClenchedTeethPixels
                                   && m.MouthAspectRatio >= _o.GrinMinMouthAspectRatio;
            bool clenchedPain = PainTeethShown(m);
            bool sleepingNow = (m.EyesClosed && !(m.EyesTightSqueezed && strongPainTeeth))
                               || (m.EyesAlmostClosed && !clenchedPain);

            if (sleepingNow)
            {
                // Dormindo prevalece como estado principal, mas a emoção continua sendo
                // avaliada sem o portão de vigília e devolvida como dado secundário.
                var secondaryRules = BuildRules(RuleContext.SleepSecondary, baseline)
                    .Select(spec => EvaluateRule(spec, m))
                    .ToList();
                var secondary = PickSecondary(secondaryRules);

                return new EmotionDecision
                {
                    EstadoPrincipal = "dormindo",
                    EstadoFisiologico = "dormindo",
                    ConfiancaFisiologica = sleepConfidence,
                    EstadoEmocional = secondary.Emotion,
                    EmocaoSecundaria = secondary.Emotion,
                    ConfiancaEmocional = secondary.Confidence,
                    Fonte = "landmarks",
                    Metricas = m,
                    Regras = secondaryRules,
                    Observacoes = new[]
                    {
                        m.EyesClosed
                            ? $"Olhos fechados (EAR dir={m.EyeAspectRight:F3}, esq={m.EyeAspectLeft:F3}; média < {_o.EyeClosedEar:F2} e cada olho < {_o.EyeClosedMaxEar:F2}) sem dentes de dor: estado principal dormindo."
                            : $"Olhos quase fechados (EAR dir={m.EyeAspectRight:F3}, esq={m.EyeAspectLeft:F3}; média < {_o.EyeClosedSoftEar:F2}) sem dentes de dor: estado principal dormindo.",
                        $"Emoção secundária '{secondary.Emotion}' avaliada ignorando o portão de vigília; dor durante o sono exige dentes à mostra com olhos apertados (sem esse padrão, o quadro é sono)."
                    }
                };
            }

            foreach (var emotion in PriorityOrder)
            {
                var rule = awakeRules.First(r => r.Emotion == emotion);
                if (!rule.Matched) continue;

                return new EmotionDecision
                {
                    EstadoPrincipal = "acordado",
                    EstadoFisiologico = "acordado",
                    ConfiancaFisiologica = Clamp01(1f - sleepConfidence),
                    EstadoEmocional = emotion,
                    EmocaoSecundaria = emotion,
                    ConfiancaEmocional = rule.Confidence,
                    Fonte = "landmarks",
                    Metricas = m,
                    Regras = awakeRules,
                    Observacoes = new[]
                    {
                        $"Regra '{emotion}' venceu pela prioridade {string.Join(" > ", PriorityOrder)}."
                    }
                };
            }

            float bestCompetitor = awakeRules.Max(r => r.Matched ? r.Confidence : 0f);
            var neutral = new RuleEvaluation(
                "neutro",
                true,
                Clamp01(1f - bestCompetitor),
                new[]
                {
                    "Nenhuma regra de emoção satisfeita.",
                    $"Maior confiança concorrente entre as regras ativas: {bestCompetitor:P0}."
                });

            var withNeutral = awakeRules.Append(neutral).ToList();

            return new EmotionDecision
            {
                EstadoPrincipal = "acordado",
                EstadoFisiologico = "acordado",
                ConfiancaFisiologica = Clamp01(1f - sleepConfidence),
                EstadoEmocional = "neutro",
                EmocaoSecundaria = "neutro",
                ConfiancaEmocional = neutral.Confidence,
                Fonte = "landmarks",
                Metricas = m,
                Regras = withNeutral,
                Observacoes = Array.Empty<string>()
            };
        }

        /// <summary>Escolhe a emoção secundária pela mesma prioridade, sem deixar o sono competir.</summary>
        private RuleEvaluation PickSecondary(IReadOnlyList<RuleEvaluation> rules)
        {
            foreach (var emotion in PriorityOrder)
            {
                var rule = rules.FirstOrDefault(r => r.Emotion == emotion);
                if (rule is { Matched: true }) return rule;
            }

            float bestCompetitor = rules.Max(r => r.Matched ? r.Confidence : 0f);
            return new RuleEvaluation(
                "neutro",
                true,
                Clamp01(1f - bestCompetitor),
                new[]
                {
                    "Nenhuma regra de emoção satisfeita na avaliação secundária.",
                    $"Maior confiança concorrente: {bestCompetitor:P0}."
                });
        }

        private RuleSpec[] BuildRules(RuleContext context, ExpressionBaseline? baseline) => context == RuleContext.SleepSecondary
            ? new[] { PainRule(context), NauseaRule(context), FearRule(context, baseline), SadnessRule(context, baseline) }
            : new[] { PainRule(context), NauseaRule(context), FearRule(context, baseline), SadnessRule(context, baseline), SleepinessRule(context) };

        // ------------------------------------------------------------------
        // Descrição declarativa das regras
        // ------------------------------------------------------------------

        /// <summary>
        /// Dor: dentes cerrados/à mostra + olhos apertados (ou fortemente cerrados),
        /// sem bocejo, mão na boca ou sorriso. Os cantos abaixados não excluem mais:
        /// caretas de dor reais também deprimem os cantos, e dor tem prioridade sobre tristeza.
        /// </summary>
        private RuleSpec PainRule(RuleContext context) => new(
            "dor",
            context == RuleContext.SleepSecondary
                ? new Criterion(
                    "olhos fechados pelo sono (avaliação secundária)",
                    _ => true,
                    m => $"EAR dir={m.EyeAspectRight:F3}, esq={m.EyeAspectLeft:F3}; a dor secundária não exige olhos abertos")
                : new Criterion(
                    "olhos apertados ou fortemente cerrados",
                    m => m.EyesPainSquinted || m.EyesTightSqueezed,
                    m => $"EAR dir={m.EyeAspectRight:F3}, esq={m.EyeAspectLeft:F3}; média <= {_o.PainEyeEar:F2} com maior olho <= {_o.PainEyeSideMaxEar:F2}, ou maior olho <= {_o.PainClenchedEyeEar:F2}"),
            new[]
            {
                new Criterion(
                    "dentes cerrados/à mostra",
                    m => PainTeethShown(m),
                    m => $"dentes={m.TeethRatio:P1} ({m.TeethPixels}px), MAR={m.MouthAspectRatio:F2}, abertura={m.MouthOpeningPx:F1}px (cerrado exige aperto ocular <= {_o.PainClenchedEyeEar:F2}; careta exige MAR >= {_o.GrimaceMinMouthAspectRatio:F2} com dentes >= {_o.MinClenchedTeethRatio:F2})")
            },
            new[]
            {
                new Criterion("bocejo ativo", m => m.Yawning, m => $"MAR={m.MouthAspectRatio:F2} >= {_o.YawnMouthAspectRatio:F2}"),
                new Criterion("mão cobrindo a boca", m => m.HandOnMouth, m => $"{m.HandPointsInMouthRegion} ponto(s) da mão na região da boca"),
                // Sorriso só exclui a dor quando os olhos NÃO estão fortemente apertados:
                // dentes cerrados (AU20) também elevam cantos/lábio em relação ao marco 13.
                new Criterion("sorriso com olhos abertos", m => (m.Smiling || ToothGrin(m)) && !m.EyesTightSqueezed,
                    m => $"cantos acima do lábio superior (queda={m.MouthCornerSagUpperLip:F3}) ou sorriso largo (dentes={m.TeethRatio:P0}, MAR={m.MouthAspectRatio:F2}) com olhos não apertados"),
                // Careta de tristeza com boca fechada (bico/beiço): a sobrancelha está
                // CAÍDA (interna erguida, cauda baixa) e erguida, o oposto da dor, que
                // puxa a sobrancelha para baixo. Sem isso, os olhos semicerrados e o
                // brilho do lábio (lido como "dentes") faziam a dor roubar a tristeza.
                new Criterion(
                    "careta de tristeza com boca fechada",
                    m => SadClosedMouth(m),
                    m => $"sobrancelha caída (inclinação={m.BrowSlantRatio:F3}, elevação={m.BrowRaiseRatio:F2}) com boca fechada (MAR={m.MouthAspectRatio:F2}): padrão de tristeza, não dor")
            },
            new (string, Func<FacialMetrics, float>)[]
            {
                ("dentes", m => Positive(m.TeethRatio, _o.TeethPixelRatio)),
                ("aperto dos olhos", m => PainNarrowConfidence(m.EyeAspectRatio))
            });

        /// <summary>Enjoo: mão cobrindo a boca, sem bocejo simultâneo.</summary>
        private RuleSpec NauseaRule(RuleContext context) => new(
            "enjoo",
            AwakeCriterion(context),
            new[]
            {
                new Criterion(
                    "mão na boca",
                    m => m.HandOnMouth,
                    m => m.HandDetected
                        ? $"{m.HandPointsInMouthRegion} ponto(s) (mínimo {_o.MinHandPointsInMouthRegion}), distância={m.HandMouthDistanceRatio:F2} IOD (máximo {_o.MaxHandMouthDistanceRatio:F2})"
                        : "nenhuma mão detectada")
            },
            new[]
            {
                new Criterion("bocejo simultâneo", m => m.Yawning, m => $"MAR={m.MouthAspectRatio:F2} >= {_o.YawnMouthAspectRatio:F2} (gesto de sono)")
            },
            new (string, Func<FacialMetrics, float>)[]
            {
                ("pontos da mão", m => Positive(m.HandPointsInMouthRegion, _o.MinHandPointsInMouthRegion)),
                ("proximidade da boca", m => ProximityConfidence(m.HandMouthDistanceRatio, _o.MaxHandMouthDistanceRatio))
            });

        /// <summary>
        /// Medo: sobrancelhas levantadas + olhos arregalados, sem mão na boca ou cantos
        /// abaixados com boca fechada. Sem repouso, olhos arregalados sozinhos não bastam
        /// (repouso da webcam já tem EAR alto): exige também sobrancelha muito inclinada
        /// (AU1) ou boca aberta, que é o que separa medo de um rosto neutro. Com repouso
        /// aprendido, o desvio precisa ser inequívoco (+0,035 de elevação com olhos não
        /// fechados, ou +0,020 de EAR com sobrancelha erguida).
        /// </summary>
        private RuleSpec FearRule(RuleContext context, ExpressionBaseline? baseline) => new(
            "medo",
            AwakeCriterion(context),
            new[]
            {
                new Criterion(
                    "sobrancelhas erguidas + olhos arregalados",
                    // Olhos arregalados com sobrancelha erguida já é medo: a inclinação da
                    // sobrancelha e a boca são irrelevantes nessa expressão (exigi-las
                    // deixava o medo de fora em webcams com inclinação baixa).
                    m => (m.EyesWide && m.BrowRaiseRatio >= 0.27f)
                         || (baseline != null && FrightDeviation(m, baseline)),
                    m => baseline == null
                        ? $"elevação={m.BrowRaiseRatio:F2} (limiar 0,27); EAR={m.EyeAspectRatio:F3} (limiar {_o.WideEyeEar:F2}); inclinação={m.BrowSlantRatio:F3}"
                        : $"elevação={m.BrowRaiseRatio:F2} vs repouso {baseline.BrowRaise:F2} (+0,035); EAR={m.EyeAspectRatio:F3} vs repouso {baseline.Ear:F3} (+0,020); absoluto: elevação={m.BrowRaiseRatio:F2} (limiar 0,27), EAR={m.EyeAspectRatio:F3} (limiar {_o.WideEyeEar:F2})")
            },
            new[]
            {
                new Criterion("mão na boca", m => m.HandOnMouth, m => $"{m.HandPointsInMouthRegion} ponto(s) da mão na boca"),
                new Criterion(
                    "cantos da boca abaixados com boca fechada",
                    m => m.Frowning && m.MouthAspectRatio < _o.GrinMinMouthAspectRatio,
                    m => $"queda={m.MouthCornerSagRatio:F3} >= {_o.FrownSagRatio:F2} e MAR={m.MouthAspectRatio:F2} < {_o.GrinMinMouthAspectRatio:F2} (tristeza)")
            },
            new (string, Func<FacialMetrics, float>)[]
            {
                ("elevação das sobrancelhas", m => MathF.Max(
                    Positive(m.BrowRaiseRatio, 0.27f),
                    baseline == null ? 0f : Clamp01(0.5f + 0.5f * (m.BrowRaiseRatio - baseline.BrowRaise) / 0.05f))),
                ("abertura dos olhos", m => MathF.Max(
                    Positive(m.EyeAspectRatio, _o.WideEyeEar),
                    baseline == null ? 0f : Clamp01(0.75f + 0.25f * (m.EyeAspectRatio - baseline.Ear) / 0.03f)))
            });

        /// <summary>
        /// Tristeza guiada pela sobrancelha: canto interno erguido com cauda externa
        /// caída (AU1, "sobrancelha caída") já basta, sem exigir a boca. A boca caída
        /// (AU15) continua valendo como evidência extra. Sem repouso: inclinação >= 0,060
        /// ou elevação interna >= 0,32. Com repouso: inclinação +0,010 ou elevação +0,035
        /// sobre o próprio rosto. O padrão leve (canto interno erguido + pálpebra caída +
        /// boca fechada) vale sempre, para expressões discretas e para quando o repouso
        /// aprendido já era o próprio rosto "triste".
        /// </summary>
        private RuleSpec SadnessRule(RuleContext context, ExpressionBaseline? baseline) => new(
            "tristeza",
            AwakeCriterion(context),
            new[]
            {
                new Criterion(
                    "sobrancelha caída (canto interno alto, cauda baixa)",
                    m => FaintSadBrow(m)
                         || (baseline == null
                             ? m.BrowSlantRatio >= 0.060f
                               || m.BrowRaiseRatio >= _o.SadBrowRaiseRatio
                             : SadnessDeviation(m, baseline)),
                    m =>
                    {
                        string faint = $"padrão leve: elevação={m.BrowRaiseRatio:F2} (limiar 0,255), inclinação={m.BrowSlantRatio:F3} (limiar 0,045), EAR={m.EyeAspectRatio:F3} (máx 0,29), MAR={m.MouthAspectRatio:F2} (máx 0,10)";
                        if (baseline == null)
                        {
                            return $"sem repouso: inclinação={m.BrowSlantRatio:F3} (limiar 0,060) ou elevação interna={m.BrowRaiseRatio:F2} (limiar {_o.SadBrowRaiseRatio:F2}); {faint}; boca sup={m.MouthCornerSagUpperLip:F3} / centro={m.MouthCornerSagRatio:F3} / interna={m.MouthCornerSagInner:F3}";
                        }

                        float slant = m.BrowSlantRatio - baseline.BrowSlant;
                        float raise = m.BrowRaiseRatio - baseline.BrowRaise;
                        float sagUpper = m.MouthCornerSagUpperLip - baseline.SagUpperLip;
                        float sagSeam = m.MouthCornerSagRatio - baseline.SagSeam;
                        float sagInner = m.MouthCornerSagInner - baseline.SagInner;
                        string dSlant = slant.ToString("+0.000;-0.000");
                        string dRaise = raise.ToString("+0.000;-0.000");
                        string dSagUpper = sagUpper.ToString("+0.000;-0.000");
                        string dSagSeam = sagSeam.ToString("+0.000;-0.000");
                        string dSagInner = sagInner.ToString("+0.000;-0.000");
                        return $"desvio do repouso: inclinação={dSlant} (limiar +0,010); elevação={dRaise} (+0,035); boca: sup={dSagUpper} / centro={dSagSeam} / interna={dSagInner} (forte +0,015/+0,008); {faint}";
                    })
            },
            new[]
            {
                new Criterion("mão na boca", m => m.HandOnMouth, m => $"{m.HandPointsInMouthRegion} ponto(s) da mão na boca"),
                new Criterion("bocejo", m => m.Yawning, m => $"MAR={m.MouthAspectRatio:F2} >= {_o.YawnMouthAspectRatio:F2}"),
                new Criterion(
                    "dentes de dor",
                    m => PainTeethShown(m) && !SadClosedMouth(m),
                    m => $"dentes={m.TeethRatio:P0}, MAR={m.MouthAspectRatio:F2}: padrão de dor (prioridade maior)"),
                // Sorriso real: cantos ACIMA do centro da boca. Não usar "m.Smiling"
                // (cantos acima do lábio superior): boca projetada/"beiço" desloca o
                // lábio superior e simula sorriso, bloqueando tristeza num rosto caído.
                // Com sobrancelha caída, o "bico" também levanta os cantos em relação à
                // comissura: isso não pode virar exclusão de sorriso da tristeza.
                new Criterion(
                    "sorriso",
                    m => (m.MouthCornerSagRatio <= -_o.SmileCornerLiftRatio || ToothGrin(m)) && !SadBrowPattern(m),
                    m => $"cantos acima do centro da boca (queda={m.MouthCornerSagRatio:F3} <= -{_o.SmileCornerLiftRatio:F2}) ou dentes de sorriso (dentes={m.TeethRatio:P0})")
            },
            new (string, Func<FacialMetrics, float>)[]
            {
                // A sobrancelha é o gatilho principal; a boca só reforça a confiança.
                ("sobrancelha caída", m =>
                {
                    float brow, mouth;
                    float faintBrow = MathF.Max(
                        (m.BrowSlantRatio - 0.045f) / 0.05f,
                        (m.BrowRaiseRatio - 0.255f) / 0.05f);
                    if (baseline == null)
                    {
                        brow = MathF.Max(
                            MathF.Max(
                                (m.BrowSlantRatio - 0.060f) / 0.03f,
                                (m.BrowRaiseRatio - _o.SadBrowRaiseRatio) / 0.05f),
                            faintBrow);
                        mouth = MathF.Max(
                            (m.MouthCornerSagUpperLip - _o.SadMouthSagRatio) / 0.03f,
                            MathF.Max(
                                (m.MouthCornerSagRatio - _o.SadMouthSagSeamRatio) / 0.03f,
                                (m.MouthCornerSagInner - 0.018f) / 0.015f));
                    }
                    else
                    {
                        brow = MathF.Max(
                            MathF.Max(
                                (m.BrowSlantRatio - baseline.BrowSlant - 0.010f) / 0.03f,
                                (m.BrowRaiseRatio - baseline.BrowRaise - 0.035f) / 0.05f),
                            faintBrow);
                        mouth = MathF.Max(
                            (m.MouthCornerSagUpperLip - baseline.SagUpperLip - 0.006f) / 0.03f,
                            MathF.Max(
                                (m.MouthCornerSagRatio - baseline.SagSeam - 0.006f) / 0.03f,
                                (m.MouthCornerSagInner - baseline.SagInner - 0.003f) / 0.015f));
                    }

                    return Clamp01(0.55f + 0.45f * MathF.Max(Clamp01(brow), 0.6f * Clamp01(mouth)));
                })
            });

        /// <summary>Sonolência: bocejo em vigília, sem mão na boca.</summary>
        private RuleSpec SleepinessRule(RuleContext context) => new(
            "sono",
            AwakeCriterion(context),
            new[]
            {
                new Criterion(
                    "bocejo",
                    m => m.Yawning,
                    m => $"MAR={m.MouthAspectRatio:F2} >= {_o.YawnMouthAspectRatio:F2} e abertura/altura={m.MouthOpenRatio:F3} >= {_o.YawnOpenRatio:F3}")
            },
            new[]
            {
                new Criterion("mão na boca", m => m.HandOnMouth, m => $"{m.HandPointsInMouthRegion} ponto(s) (enjoo)")
            },
            new (string, Func<FacialMetrics, float>)[]
            {
                ("abertura vertical (MAR)", m => Positive(m.MouthAspectRatio, _o.YawnMouthAspectRatio)),
                ("abertura relativa ao rosto", m => Positive(m.MouthOpenRatio, _o.YawnOpenRatio))
            });

        private Criterion AwakeCriterion(RuleContext context) => context == RuleContext.SleepSecondary
            ? new Criterion(
                "avaliação secundária durante o sono",
                _ => true,
                _ => "olhos fechados; o estado dormindo não bloqueia a emoção secundária")
            : new Criterion(
                "acordado (não dormindo)",
                m => !m.EyesClosed,
                m => $"EAR dir={m.EyeAspectRight:F3}, esq={m.EyeAspectLeft:F3}; dormindo exige média < {_o.EyeClosedEar:F2} e cada olho < {_o.EyeClosedMaxEar:F2}");

        // ------------------------------------------------------------------
        // Avaliador único (mesma estrutura para todas as classes)
        // ------------------------------------------------------------------

        private static RuleEvaluation EvaluateRule(RuleSpec spec, FacialMetrics m)
        {
            var evidence = new List<string>();

            bool eyesOk = spec.Eyes.Test(m);
            evidence.Add($"{(eyesOk ? "OK" : "EXCLUÍDA")} — {spec.Eyes.Label}: {spec.Eyes.Detail(m)}");

            bool blocked = false;
            foreach (var exclusion in spec.Exclusions)
            {
                bool present = exclusion.Test(m);
                if (present)
                {
                    blocked = true;
                    evidence.Add($"EXCLUÍDA — {exclusion.Label}: {exclusion.Detail(m)}");
                }
                else
                {
                    evidence.Add($"OK — sem {exclusion.Label}");
                }
            }

            bool unmet = false;
            foreach (var requirement in spec.Required)
            {
                bool ok = requirement.Test(m);
                if (!ok) unmet = true;
                evidence.Add($"{(ok ? "OK" : "PENDENTE")} — {requirement.Label}: {requirement.Detail(m)}");
            }

            bool matched = eyesOk && !blocked && !unmet;
            float confidence = 0f;

            if (matched)
            {
                confidence = 1f;
                foreach (var (label, score) in spec.ConfidenceSignals)
                {
                    float value = Clamp01(score(m));
                    confidence = MathF.Min(confidence, value);
                    evidence.Add($"confiança — {label}: {value:P0}");
                }
            }
            else
            {
                evidence.Add($"confiança — {spec.Emotion}: 0% (regra não satisfeita)");
            }

            return new RuleEvaluation(spec.Emotion, matched, confidence, evidence);
        }

        private sealed record Criterion(string Label, Func<FacialMetrics, bool> Test, Func<FacialMetrics, string> Detail);

        /// <summary>
        /// Sobrancelha caída com elevação real do canto interno: evidência de tristeza
        /// (AU1) que não combina com sorriso. Usada para impedir que "bico" e brilho de
        /// lábio transformem a tristeza em sorriso ou em dor.
        /// </summary>
        private static bool SadBrowPattern(FacialMetrics m)
            => m.BrowSlantRatio >= 0.060f && m.BrowRaiseRatio >= 0.22f;

        /// <summary>
        /// Tristeza discreta: canto interno erguido com pálpebra caída e boca fechada,
        /// sem forçar a careta. Separa rostos neutros de olhos abertos (elevação alta
        /// com EAR ~0,32+) da tristeza leve (EAR <= 0,29).
        /// </summary>
        private static bool FaintSadBrow(FacialMetrics m)
            => m.BrowRaiseRatio >= 0.255f
               && m.BrowSlantRatio >= 0.045f
               && m.EyeAspectRatio <= 0.29f
               && m.MouthAspectRatio < 0.10f;

        /// <summary>Careta de tristeza de boca fechada (bico/beiço sem abertura).</summary>
        private static bool SadClosedMouth(FacialMetrics m)
            => m.MouthAspectRatio < 0.10f
               && ((m.BrowSlantRatio >= 0.075f && m.BrowRaiseRatio >= 0.25f) || FaintSadBrow(m));

        /// <summary>
        /// Padrões de dentes à mostra na dor: mandíbula travada (MAR baixo), boca
        /// entreaberta e careta com a boca aberta. A boca travada/entreaberta exige
        /// aperto ocular forte (o olho MAIS ABERTO ainda apertado); a careta aceita
        /// também o aperto de dor comum (olhos franzidos, não necessariamente cerrados),
        /// porque caretas reais alternam entre esses níveis de aperto.
        /// </summary>
        private bool PainTeethShown(FacialMetrics m)
        {
            bool squeezed = m.EyesTightSqueezed;
            bool squinted = squeezed || m.EyesPainSquinted;
            bool bandTeeth = m.TeethRatio >= _o.MinClenchedTeethRatio
                             && m.TeethPixels >= _o.MinClenchedTeethPixels;
            bool strongTeeth = m.TeethRatio >= _o.StrongTeethRatio
                               && m.TeethPixels >= _o.MinClenchedTeethPixels;
            // Uma faixa de dentes muito clara (dentes à mostra) dispensa a abertura mínima
            // dos lábios: mandíbula travada pode exibir dentes com a boca quase fechada.
            bool bandOpen = bandTeeth
                            && (m.MouthOpeningPx >= _o.MinClenchedOpenPx || strongTeeth);

            bool clenched = squeezed
                            && m.MouthAspectRatio <= _o.TeethMaxMouthAspectRatio
                            && bandOpen;

            bool exposed = squeezed
                           && m.MouthAspectRatio > _o.TeethMaxMouthAspectRatio
                           && m.MouthAspectRatio < _o.GrimaceMinMouthAspectRatio
                           && bandOpen;

            bool grimace = squinted
                           && m.MouthAspectRatio >= _o.GrimaceMinMouthAspectRatio
                           && m.MouthAspectRatio < _o.YawnMouthAspectRatio
                           && bandTeeth;

            return clenched || exposed || grimace;
        }

        /// <summary>
        /// Sorriso largo: faixa clara de dentes com a boca aberta E cantos levantados.
        /// A exigência dos cantos para cima evita que uma careta de dor (dentes à mostra,
        /// cantos deprimidos) seja excluída da regra de dor como se fosse sorriso.
        /// </summary>
        private bool ToothGrin(FacialMetrics m)
            => m.TeethRatio >= _o.GrinTeethRatio
               && m.MouthAspectRatio >= _o.GrinMinMouthAspectRatio
               && m.MouthCornerSagUpperLip < 0f;

        /// <summary>
        /// Desvio de tristeza em relação ao repouso. Sobrancelha sozinha basta:
        /// inclinação +0,010 (canto interno subiu / cauda caiu) ou elevação interna
        /// +0,035. A boca caída continua como gatilho independente.
        /// </summary>
        private static bool SadnessDeviation(FacialMetrics m, ExpressionBaseline b)
        {
            float slant = m.BrowSlantRatio - b.BrowSlant;
            float raise = m.BrowRaiseRatio - b.BrowRaise;
            float sagUpper = m.MouthCornerSagUpperLip - b.SagUpperLip;
            float sagSeam = m.MouthCornerSagRatio - b.SagSeam;
            float sagInner = m.MouthCornerSagInner - b.SagInner;

            bool browSad = slant >= 0.010f || raise >= 0.035f;
            bool mouthStrong = sagUpper >= 0.015f || sagSeam >= 0.015f || sagInner >= 0.008f;

            return browSad || mouthStrong;
        }

        /// <summary>
        /// Desvio de medo em relação ao repouso: sobrancelha bem acima do normal com os
        /// olhos não mais fechados que o repouso, ou olhos claramente mais abertos com a
        /// sobrancelha erguida. Evita disparar com a pequena variação do rosto neutro.
        /// </summary>
        private static bool FrightDeviation(FacialMetrics m, ExpressionBaseline b)
        {
            float brow = m.BrowRaiseRatio - b.BrowRaise;
            float eye = m.EyeAspectRatio - b.Ear;

            return (brow >= 0.035f && eye >= -0.010f)
                   || (eye >= 0.020f && brow >= 0.010f);
        }

        private sealed record RuleSpec(
            string Emotion,
            Criterion Eyes,
            IReadOnlyList<Criterion> Required,
            IReadOnlyList<Criterion> Exclusions,
            IReadOnlyList<(string Label, Func<FacialMetrics, float> Score)> ConfidenceSignals);

        // ------------------------------------------------------------------
        // Funções de confiança compartilhadas
        // ------------------------------------------------------------------

        /// <summary>0,5 no limiar, aproximando-se de 1,0 conforme o valor cresce acima dele.</summary>
        private static float Positive(float value, float threshold)
            => Clamp01(0.5f + 0.5f * (value - threshold) / MathF.Max(threshold, 0.01f));

        /// <summary>0,5 no limiar de aperto da dor, aproximando-se de 1,0 conforme o EAR cai em direção ao fechamento.</summary>
        private float PainNarrowConfidence(float ear)
        {
            float range = MathF.Max(_o.PainEyeEar - _o.EyeClosedEar, 1e-3f);
            return Clamp01(0.5f + 0.5f * (_o.PainEyeEar - ear) / range);
        }

        /// <summary>0,5 na distância máxima aceita, aproximando-se de 1,0 conforme a mão se aproxima da boca.</summary>
        private static float ProximityConfidence(float distance, float maxDistance)
            => Clamp01(0.5f + 0.5f * (maxDistance - distance) / MathF.Max(maxDistance, 0.01f));

        private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    }
}
