using System;
using System.Collections.Generic;
using System.Linq;
using PerceptAI.API.Models;

namespace PerceptAI.API.Services
{
    
    public sealed class EmotionRuleEngine
    {
        private static readonly string[] PriorityOrder = { "dor", "enjoo", "medo", "tristeza", "sono" };

        private readonly EmotionRuleOptions _o;

        public EmotionRuleEngine(EmotionRuleOptions options) => _o = options;

        public EmotionDecision Evaluate(FacialMetrics m)
        {
            var rules = new List<RuleEvaluation>
            {
                EvaluatePain(m),
                EvaluateNausea(m),
                EvaluateFear(m),
                EvaluateSadness(m),
                EvaluateSleepiness(m)
            };

            float sleepConfidence = Clamp01(0.5f + 0.5f * (_o.EyeClosedEar - m.EyeAspectRatio) / MathF.Max(_o.EyeClosedEar, 0.01f));

            if (m.EyesClosed)
            {
                bool sleeping = m.MouthAspectRatio < _o.SleepMaxMouthAspectRatio
                                && !m.HandOnMouth
                                && !m.TeethClenched;

                if (sleeping)
                {
                    
                    return new EmotionDecision
                    {
                        EstadoFisiologico = "dormindo",
                        ConfiancaFisiologica = sleepConfidence,
                        EstadoEmocional = null,
                        ConfiancaEmocional = null,
                        Fonte = "landmarks",
                        Metricas = m,
                        Regras = rules,
                        Observacoes = new[] { "Olhos fechados: estado dormindo anula todas as emoções." }
                    };
                }
            }

            foreach (var emotion in PriorityOrder)
            {
                var rule = rules.First(r => r.Emotion == emotion);
                if (!rule.Matched) continue;

                return new EmotionDecision
                {
                    EstadoFisiologico = "acordado",
                    ConfiancaFisiologica = Clamp01(1f - sleepConfidence),
                    EstadoEmocional = emotion,
                    ConfiancaEmocional = rule.Confidence,
                    Fonte = "landmarks",
                    Metricas = m,
                    Regras = rules,
                    Observacoes = Array.Empty<string>()
                };
            }

            float maxConfidence = rules.Max(r => r.Confidence);
            var neutral = new RuleEvaluation(
                "neutro",
                true,
                Clamp01(1f - maxConfidence),
                new[] { $"Nenhuma regra satisfeita (maior confiança concorrente {maxConfidence:P0})." });

            rules.Add(neutral);

            return new EmotionDecision
            {
                EstadoFisiologico = "acordado",
                ConfiancaFisiologica = Clamp01(1f - sleepConfidence),
                EstadoEmocional = "neutro",
                ConfiancaEmocional = neutral.Confidence,
                Fonte = "landmarks",
                Metricas = m,
                Regras = rules,
                Observacoes = Array.Empty<string>()
            };
        }

        private RuleEvaluation EvaluatePain(FacialMetrics m)
        {
            float teethConfidence = Positive(m.TeethRatio, _o.TeethPixelRatio);
            float eyeConfidence = NarrowedConfidence(m.EyeAspectRatio);
            bool eyesSquinted = m.EyesNarrowed || m.EyesClosed;
            bool matched = m.TeethClenched && eyesSquinted && !m.Yawning && !m.HandOnMouth;
            float confidence = matched ? MathF.Min(teethConfidence, eyeConfidence) : 0f;

            var evidence = new List<string>
            {
                $"dentes: {m.TeethRatio:P1} dos pixels da boca ({m.TeethPixels}px), limiar {_o.TeethPixelRatio:P0}",
                $"olhos: EAR={m.EyeAspectRatio:F3}, apertado se EAR < {_o.EyeNarrowEar:F2}",
                $"boca: MAR={m.MouthAspectRatio:F2}, válido até {_o.TeethMaxMouthAspectRatio:F2}"
            };
            if (m.Yawning) evidence.Add("excluída: bocejo ativo (boca muito aberta)");
            if (m.HandOnMouth) evidence.Add("excluída: mão cobrindo a boca (leitura de dentes não confiável)");

            return new RuleEvaluation("dor", matched, confidence, evidence);
        }

        private RuleEvaluation EvaluateNausea(FacialMetrics m)
        {
            float confidence = Positive(m.HandPointsInMouthRegion, _o.MinHandPointsInMouthRegion);
            bool matched = m.HandOnMouth && !m.Yawning;

            var evidence = new List<string>
            {
                m.HandDetected
                    ? $"mão: {m.HandPointsInMouthRegion} ponto(s) na região da boca, mínimo {_o.MinHandPointsInMouthRegion}"
                    : "mão: nenhuma mão detectada",
                $"distância mão-boca: {m.HandMouthDistanceRatio:F2} x distância interocular"
            };
            if (m.HandOnMouth && m.Yawning) evidence.Add("excluída: bocejo com mão na boca é classificado como sonolência");

            return new RuleEvaluation("enjoo", matched, matched ? confidence : 0f, evidence);
        }

        private RuleEvaluation EvaluateFear(FacialMetrics m)
        {
            float confidence = Positive(m.BrowRaiseRatio, _o.BrowRaiseRatio);
            bool matched = m.BrowsRaised && !m.TeethVisible && m.EyesOpen;

            var evidence = new List<string>
            {
                $"sobrancelhas: elevação={m.BrowRaiseRatio:F2} (x interocular), limiar {_o.BrowRaiseRatio:F2}",
                m.TeethVisible
                    ? $"excluída: dentes visíveis ({m.TeethRatio:P1})"
                    : "dentes não visíveis (requisito atendido)",
                m.EyesOpen ? "olhos abertos (requisito atendido)" : "excluída: olhos fechados/apertados"
            };

            return new RuleEvaluation("medo", matched, matched ? confidence : 0f, evidence);
        }

        private RuleEvaluation EvaluateSadness(FacialMetrics m)
        {
            float confidence = Positive(m.MouthCornerSagRatio, _o.FrownSagRatio);
            bool matched = m.Frowning;

            var evidence = new List<string>
            {
                $"cantos da boca: queda={m.MouthCornerSagRatio:F3} (x largura da boca), limiar {_o.FrownSagRatio:F2}",
                $"abertura da boca: MAR={m.MouthAspectRatio:F2}"
            };

            return new RuleEvaluation("tristeza", matched, matched ? confidence : 0f, evidence);
        }

        private RuleEvaluation EvaluateSleepiness(FacialMetrics m)
        {
            float marConfidence = Positive(m.MouthAspectRatio, _o.YawnMouthAspectRatio);
            float openConfidence = Positive(m.MouthOpenRatio, _o.YawnOpenRatio);
            bool matched = m.Yawning && !m.Frowning;
            float confidence = matched ? MathF.Min(marConfidence, openConfidence) : 0f;

            var evidence = new List<string>
            {
                $"bocejo: MAR={m.MouthAspectRatio:F2} (limiar {_o.YawnMouthAspectRatio:F2})",
                $"abertura/altura do rosto={m.MouthOpenRatio:F3} (limiar {_o.YawnOpenRatio:F3})"
            };
            if (m.Yawning && m.Frowning) evidence.Add("excluída: cantos abaixados indicam choro/tristeza");

            return new RuleEvaluation("sono", matched, confidence, evidence);
        }

        private float NarrowedConfidence(float ear)
        {
            float range = MathF.Max(_o.EyeNarrowEar - _o.EyeClosedEar, 1e-3f);
            return Clamp01(0.5f + 0.5f * (_o.EyeNarrowEar - ear) / range);
        }

        private static float Positive(float value, float threshold)
            => Clamp01(0.5f + 0.5f * (value - threshold) / MathF.Max(threshold, 0.01f));

        private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    }
}
