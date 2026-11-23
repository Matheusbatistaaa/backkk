using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using PerceptAI.API.ML;
using PerceptAI.API.Models;

namespace PerceptAI.API.Services
{
    public class DualStateResult
    {
        public string EstadoFisiologico { get; set; } = "acordado";
        public float ConfiancaFisiologica { get; set; }
        public string? EstadoEmocional { get; set; }
        public float? ConfiancaEmocional { get; set; }
        public string PrimaryEmotion { get; set; } = "neutro";
        public float PrimaryConfidence { get; set; }
        public string Fonte { get; set; } = "landmarks";
        public FacialMetrics? Metricas { get; set; }
        public IReadOnlyList<RuleEvaluation> Regras { get; set; } = Array.Empty<RuleEvaluation>();
        public IReadOnlyList<string> Observacoes { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Orquestra o pipeline único de análise:
    /// imagem -> marcos (rosto/mãos) -> métricas -> regras -> decisão.
    /// Sem rosto/marcos, cai para o classificador legado de 8 classes.
    /// </summary>
    public sealed class EmotionDetectionService
    {
        private const int ClassifierInputSize = 224;

        private static readonly string[] LegacyClasses =
            { "dor", "enjoo", "medo", "sono", "tristeza", "neutro", "acordado", "dormindo" };

        private readonly ModelLoader _models;
        private readonly LandmarkDetectionService _landmarks;
        private readonly FacialMetricsService _metrics;
        private readonly EmotionRuleEngine _rules;

        public EmotionDetectionService(
            ModelLoader models,
            LandmarkDetectionService landmarks,
            FacialMetricsService metrics,
            EmotionRuleEngine rules)
        {
            _models = models;
            _landmarks = landmarks;
            _metrics = metrics;
            _rules = rules;
        }

        public bool LandmarkPipelineAvailable => _landmarks.Available;

        public DualStateResult AnalyzeDualState(ImageRgb24 image)
        {
            if (_landmarks.Available)
            {
                var frame = _landmarks.Detect(image);
                if (frame.Face != null)
                {
                    var metrics = _metrics.Compute(image, frame.Face, frame.Hands);
                    var decision = _rules.Evaluate(metrics);
                    return Map(decision);
                }

                string reason = frame.FacesDetected == 0
                    ? "Nenhum rosto detectado; usando classificador como fallback."
                    : "Face Mesh rejeitado pelo score mínimo; usando classificador como fallback.";
                return ClassifierFallback(image, reason);
            }

            return ClassifierFallback(image, "Pipeline de landmarks indisponível; usando classificador como fallback.");
        }

        public (string Emotion, float Confidence) DetectEmotion(ImageRgb24 image)
        {
            var result = AnalyzeDualState(image);
            return (result.PrimaryEmotion, result.PrimaryConfidence);
        }

        /// <summary>Probabilidades do classificador legado, apenas para diagnóstico/tela de teste.</summary>
        public Dictionary<string, float> GetAllProbabilities(ImageRgb24 image)
        {
            var dict = new Dictionary<string, float>();
            if (!_models.HasClassifier) return dict;

            var probabilities = ClassifierProbabilities(image);
            for (int i = 0; i < LegacyClasses.Length && i < probabilities.Length; i++)
                dict[LegacyClasses[i]] = probabilities[i];
            return dict;
        }

        private DualStateResult ClassifierFallback(ImageRgb24 image, string reason)
        {
            if (!_models.HasClassifier)
                throw new InvalidOperationException(
                    $"{reason} O classificador legado também não está disponível em ML/.");

            var probabilities = ClassifierProbabilities(image);
            int best = 0;
            for (int i = 1; i < probabilities.Length; i++)
                if (probabilities[i] > probabilities[best]) best = i;

            float confidence = probabilities[best];
            string cls = LegacyClasses[Math.Min(best, LegacyClasses.Length - 1)];

            var result = new DualStateResult
            {
                Fonte = "classificador",
                PrimaryConfidence = confidence,
                Observacoes = new[] { reason }
            };

            switch (cls)
            {
                case "dormindo":
                    result.EstadoFisiologico = "dormindo";
                    result.ConfiancaFisiologica = confidence;
                    result.EstadoEmocional = null;
                    result.ConfiancaEmocional = null;
                    result.PrimaryEmotion = "dormindo";
                    break;
                case "acordado":
                    result.EstadoFisiologico = "acordado";
                    result.ConfiancaFisiologica = confidence;
                    result.EstadoEmocional = "neutro";
                    result.ConfiancaEmocional = confidence;
                    result.PrimaryEmotion = "neutro";
                    break;
                default:
                    result.EstadoFisiologico = "acordado";
                    result.ConfiancaFisiologica = 1f - confidence;
                    result.EstadoEmocional = cls;
                    result.ConfiancaEmocional = confidence;
                    result.PrimaryEmotion = cls;
                    break;
            }

            return result;
        }

        private float[] ClassifierProbabilities(ImageRgb24 image)
        {
            var resized = ImageWarp.ResizeStretch(image, ClassifierInputSize, ClassifierInputSize);
            var inputName = _models.EmotionClassifier!.InputMetadata.Keys.First();
            using var results = _models.EmotionClassifier.Run(
                new[] { NamedOnnxValue.CreateFromTensor(inputName, TensorBuilder.ImageNetChw(resized)) });

            var output = results.First().AsTensor<float>().ToArray();
            return Softmax(output);
        }

        private static float[] Softmax(float[] logits)
        {
            float max = logits.Max();
            double sum = logits.Sum(v => Math.Exp(v - max));
            return logits.Select(v => (float)(Math.Exp(v - max) / sum)).ToArray();
        }

        private static DualStateResult Map(EmotionDecision decision) => new()
        {
            EstadoFisiologico = decision.EstadoFisiologico,
            ConfiancaFisiologica = decision.ConfiancaFisiologica,
            EstadoEmocional = decision.EstadoEmocional,
            ConfiancaEmocional = decision.ConfiancaEmocional,
            PrimaryEmotion = decision.EstadoEmocional ?? "dormindo",
            PrimaryConfidence = decision.ConfiancaEmocional ?? decision.ConfiancaFisiologica,
            Fonte = decision.Fonte,
            Metricas = decision.Metricas,
            Regras = decision.Regras,
            Observacoes = decision.Observacoes
        };
    }
}
