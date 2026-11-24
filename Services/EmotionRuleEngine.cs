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

        public EmotionDecision Evaluate(FacialMetrics m)
        {
            var awakeRules = BuildRules(RuleContext.Awake)
                .Select(spec => EvaluateRule(spec, m))
                .ToList();

            // Confiança de sono cresce conforme o EAR médio cai abaixo do limiar de fechamento.
            float sleepConfidence = Clamp01(0.5f + 0.5f * (_o.EyeClosedEar - m.EyeAspectRatio)
                                                   / MathF.Max(_o.EyeClosedEar, 0.01f));

            // Na faixa ambígua (quase fechado) dentes cerrados de dor desempatam a favor da vigília;
            // sem esse padrão, o quadro continua sendo tratado como sono.
            bool clenchedPain = PainTeethShown(m, allowGrimace: false);
            bool sleepingNow = m.EyesClosed || (m.EyesAlmostClosed && !clenchedPain);

            if (sleepingNow)
            {
                // Dormindo prevalece como estado principal, mas a emoção continua sendo
                // avaliada sem o portão de vigília e devolvida como dado secundário.
                var secondaryRules = BuildRules(RuleContext.SleepSecondary)
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
                            ? $"Olhos fechados (EAR dir={m.EyeAspectRight:F3}, esq={m.EyeAspectLeft:F3}; média < {_o.EyeClosedEar:F2} e cada olho < {_o.EyeClosedMaxEar:F2}): estado principal dormindo."
                            : $"Olhos quase fechados (EAR dir={m.EyeAspectRight:F3}, esq={m.EyeAspectLeft:F3}; média < {_o.EyeClosedSoftEar:F2}) sem dentes cerrados de dor: estado principal dormindo.",
                        $"Emoção secundária '{secondary.Emotion}' avaliada ignorando o portão de vigília; dor durante o sono exige dentes cerrados (careta de boca aberta não conta)."
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

        private RuleSpec[] BuildRules(RuleContext context) => context == RuleContext.SleepSecondary
            ? new[] { PainRule(context), NauseaRule(context), FearRule(context), SadnessRule(context) }
            : new[] { PainRule(context), NauseaRule(context), FearRule(context), SadnessRule(context), SleepinessRule(context) };

        // ------------------------------------------------------------------
        // Descrição declarativa das regras
        // ------------------------------------------------------------------

        /// <summary>
        /// Dor: dentes cerrados/à mostra + olhos apertados, sem bocejo, mão na boca,
        /// sorriso ou boca puxada para baixo. Os cantos abaixados separam tristeza de dor.
        /// </summary>
        private RuleSpec PainRule(RuleContext context) => new(
            "dor",
            context == RuleContext.SleepSecondary
                ? new Criterion(
                    "olhos fechados pelo sono (avaliação secundária)",
                    _ => true,
                    m => $"EAR dir={m.EyeAspectRight:F3}, esq={m.EyeAspectLeft:F3}; a dor secundária não exige olhos abertos")
                : new Criterion(
                    "olhos apertados",
                    m => !m.EyesClosed
                         && m.EyeAspectRight <= _o.PainEyeEar
                         && m.EyeAspectLeft <= _o.PainEyeEar,
                    m => $"EAR dir={m.EyeAspectRight:F3}, esq={m.EyeAspectLeft:F3}; apertado exige ambos <= {_o.PainEyeEar:F2} e não fechados (média >= {_o.EyeClosedEar:F2})"),
            new[]
            {
                new Criterion(
                    "dentes cerrados/à mostra",
                    m => PainTeethShown(m, allowGrimace: context == RuleContext.Awake),
                    m => $"dentes={m.TeethRatio:P1} ({m.TeethPixels}px), MAR={m.MouthAspectRatio:F2}, abertura={m.MouthOpeningPx:F1}px (cerrado/entreaberto exige aperto ocular <= {_o.PainClenchedEyeEar:F2} e abertura >= {_o.MinClenchedOpenPx:F1}px; careta exige MAR >= {_o.GrimaceMinMouthAspectRatio:F2})")
            },
            new[]
            {
                new Criterion("bocejo ativo", m => m.Yawning, m => $"MAR={m.MouthAspectRatio:F2} >= {_o.YawnMouthAspectRatio:F2}"),
                new Criterion("mão cobrindo a boca", m => m.HandOnMouth, m => $"{m.HandPointsInMouthRegion} ponto(s) da mão na região da boca"),
                new Criterion("sorriso", m => m.Smiling || ToothGrin(m), m => $"cantos acima da comissura (queda={m.MouthCornerSagRatio:F3}) ou sorriso largo (dentes={m.TeethRatio:P0}, MAR={m.MouthAspectRatio:F2})"),
                new Criterion(
                    "boca puxada para baixo (tristeza)",
                    m => m.MouthCornerSagRatio >= _o.PainMaxFrownSag,
                    m => $"queda dos cantos={m.MouthCornerSagRatio:F3}; dor exige < {_o.PainMaxFrownSag:F3}")
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
        /// Medo: sobrancelhas levantadas E olhos arregalados nos dois olhos, sem dentes
        /// cerrados (mandíbula travada), mão na boca ou cantos abaixados.
        /// </summary>
        private RuleSpec FearRule(RuleContext context) => new(
            "medo",
            AwakeCriterion(context),
            new[]
            {
                new Criterion(
                    "sobrancelhas levantadas",
                    m => m.BrowsRaised,
                    m => $"elevação={m.BrowRaiseRatio:F2} (x IOD), limiar {_o.BrowRaiseRatio:F2}"),
                new Criterion(
                    "olhos arregalados",
                    m => m.EyesWide,
                    m => $"EAR dir={m.EyeAspectRight:F3}, esq={m.EyeAspectLeft:F3}; média >= {_o.WideEyeEar:F2} e cada olho >= {_o.MinWideEyeSideEar:F2}")
            },
            new[]
            {
                new Criterion("mão na boca", m => m.HandOnMouth, m => $"{m.HandPointsInMouthRegion} ponto(s) da mão na boca"),
                new Criterion("cantos da boca abaixados", m => m.Frowning, m => $"queda={m.MouthCornerSagRatio:F3} >= {_o.FrownSagRatio:F2} (tristeza)")
            },
            new (string, Func<FacialMetrics, float>)[]
            {
                ("elevação das sobrancelhas", m => Positive(m.BrowRaiseRatio, _o.BrowRaiseRatio)),
                ("abertura dos olhos", m => Positive(m.EyeAspectRatio, _o.WideEyeEar))
            });

        /// <summary>Tristeza: cantos da boca abaixo da comissura, sem mão na boca e sem bocejo.</summary>
        private RuleSpec SadnessRule(RuleContext context) => new(
            "tristeza",
            AwakeCriterion(context),
            new[]
            {
                new Criterion(
                    "boca para baixo",
                    m => m.Frowning,
                    m => $"queda dos cantos={m.MouthCornerSagRatio:F3} (x largura da boca), limiar {_o.FrownSagRatio:F2}")
            },
            new[]
            {
                new Criterion("mão na boca", m => m.HandOnMouth, m => $"{m.HandPointsInMouthRegion} ponto(s) da mão na boca"),
                new Criterion("bocejo", m => m.Yawning, m => $"MAR={m.MouthAspectRatio:F2} >= {_o.YawnMouthAspectRatio:F2}")
            },
            new (string, Func<FacialMetrics, float>)[]
            {
                ("queda dos cantos", m => Positive(m.MouthCornerSagRatio, _o.FrownSagRatio))
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
        /// Três padrões de dentes à mostra: mandíbula travada com faixa de dentes
        /// (MAR baixo), boca entreaberta moderada e careta de dor com boca bem aberta.
        /// Os dois primeiros exigem aperto ocular forte e abertura real dos lábios, para
        /// não confundir lábios entreabertos de um rosto neutro com dor. Um sorriso
        /// largo é excluído à parte por <see cref="ToothGrin"/>.
        /// A careta só é aceita em vigília; durante o sono apenas dentes cerrados contam.
        /// </summary>
        private bool PainTeethShown(FacialMetrics m, bool allowGrimace)
        {
            bool squeezed = MathF.Max(m.EyeAspectRight, m.EyeAspectLeft) <= _o.PainClenchedEyeEar;
            bool bandOpen = m.MouthOpeningPx >= _o.MinClenchedOpenPx
                            && m.TeethRatio >= _o.MinClenchedTeethRatio
                            && m.TeethPixels >= _o.MinClenchedTeethPixels;

            bool clenched = squeezed
                            && m.MouthAspectRatio <= _o.TeethMaxMouthAspectRatio
                            && bandOpen;

            bool exposed = squeezed
                           && m.MouthAspectRatio > _o.TeethMaxMouthAspectRatio
                           && m.MouthAspectRatio < _o.GrimaceMinMouthAspectRatio
                           && bandOpen;

            bool grimace = allowGrimace
                           && m.MouthAspectRatio >= _o.GrimaceMinMouthAspectRatio
                           && m.MouthAspectRatio < _o.YawnMouthAspectRatio
                           && m.TeethRatio >= _o.TeethPixelRatio
                           && m.TeethPixels >= _o.MinTeethPixels;

            return clenched || exposed || grimace;
        }

        private bool ToothGrin(FacialMetrics m)
            => m.TeethRatio >= _o.GrinTeethRatio && m.MouthAspectRatio >= _o.GrinMinMouthAspectRatio;

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
