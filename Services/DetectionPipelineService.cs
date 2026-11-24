using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using PerceptAI.API.Models;

namespace PerceptAI.API.Services
{
    /// <summary>Resultado padronizado do pipeline, independente da fonte usada.</summary>
    public sealed record DetectionOutcome(
        EmotionDecision Decision,
        IReadOnlyDictionary<string, float>? ClassifierProbabilities,
        int FacesDetected);

    /// <summary>
    /// Orquestra a análise de um quadro:
    ///   1) marcos faciais/manuais (BlazeFace + Face Mesh + BlazePalm + Hand Landmark);
    ///   2) métricas geométricas (FacialMetricsService);
    ///   3) decisão determinística (EmotionRuleEngine).
    /// Se o pipeline de marcos não estiver disponível, recorre ao classificador ONNX
    /// e marca a origem como "classificador". Sem rosto => "indeterminado".
    /// </summary>
    public sealed class DetectionPipelineService
    {
        private readonly LandmarkDetectionService _landmarks;
        private readonly FacialMetricsService _metrics;
        private readonly EmotionRuleEngine _rules;
        private readonly EmotionDetectionService _classifier;
        private readonly ImagePreprocessingService _preprocessing;
        private readonly EmotionRuleOptions _options;
        private readonly ILogger<DetectionPipelineService> _logger;

        public DetectionPipelineService(
            LandmarkDetectionService landmarks,
            FacialMetricsService metrics,
            EmotionRuleEngine rules,
            EmotionDetectionService classifier,
            ImagePreprocessingService preprocessing,
            EmotionRuleOptions options,
            ILogger<DetectionPipelineService> logger)
        {
            _landmarks = landmarks;
            _metrics = metrics;
            _rules = rules;
            _classifier = classifier;
            _preprocessing = preprocessing;
            _options = options;
            _logger = logger;
        }

        public bool LandmarksAvailable => _landmarks.Available;

        public string PipelineName => _landmarks.Available ? "landmarks" : "classificador";

        public DetectionOutcome Analyze(string base64Image, bool includeClassifierProbabilities = false)
        {
            ImageRgb24 image;
            try
            {
                image = ImageRgb24.FromBase64(base64Image);
            }
            catch (FormatException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FormatException($"Imagem inválida: {ex.Message}", ex);
            }

            if (!_landmarks.Available)
            {
                _logger.LogWarning("Pipeline de landmarks indisponível; usando classificador ONNX.");
                return Classify(base64Image, includeClassifierProbabilities, facesDetected: 0);
            }

            var frame = _landmarks.Detect(image);
            if (!frame.Available)
                return Classify(base64Image, includeClassifierProbabilities, frame.FacesDetected);

            if (frame.Face == null)
            {
                return Indeterminate(
                    frame.FacesDetected,
                    frame.FacesDetected == 0
                        ? "Nenhum rosto detectado no quadro."
                        : "Rosto detectado, mas o Face Mesh ficou abaixo do score mínimo.");
            }

            if (frame.Face.Bounds.Width < _options.MinFaceWidthPx)
            {
                return Indeterminate(
                    frame.FacesDetected,
                    $"Rosto muito pequeno ({frame.Face.Bounds.Width:F0}px < {_options.MinFaceWidthPx:F0}px); métricas não confiáveis.");
            }

            FacialMetrics metrics = _metrics.Compute(image, frame.Face, frame.Hands);
            EmotionDecision decision = _rules.Evaluate(metrics);

            if (frame.FacesDetected > 1)
            {
                decision = AppendObservation(
                    decision,
                    $"{frame.FacesDetected} rostos detectados; analisado o de maior área.");
            }

            _logger.LogInformation(
                "Decisão landmarks: {Fisio} ({ConfFisio:P0}) | emoção={Emocao} ({ConfEmocao}) | rostos={Faces} mãos={Hands}",
                decision.EstadoFisiologico,
                decision.ConfiancaFisiologica,
                decision.EstadoEmocional ?? "-",
                decision.ConfiancaEmocional?.ToString("P0") ?? "-",
                frame.FacesDetected,
                frame.Hands.Length);

            return new DetectionOutcome(
                decision,
                includeClassifierProbabilities ? TryClassifierProbabilities(base64Image) : null,
                frame.FacesDetected);
        }

        private DetectionOutcome Indeterminate(int facesDetected, string reason)
        {
            var decision = new EmotionDecision
            {
                EstadoPrincipal = "indeterminado",
                EstadoFisiologico = "indeterminado",
                ConfiancaFisiologica = 0f,
                EstadoEmocional = null,
                ConfiancaEmocional = null,
                EmocaoSecundaria = null,
                Fonte = "landmarks",
                Observacoes = new[] { reason }
            };

            _logger.LogWarning("Análise indeterminada: {Reason}", reason);
            return new DetectionOutcome(decision, null, facesDetected);
        }

        private DetectionOutcome Classify(string base64Image, bool includeProbabilities, int facesDetected)
        {
            float[] normalized = _preprocessing.PreprocessBase64Image(base64Image);
            DualStateResult dual = _classifier.AnalyzeDualState(normalized);

            bool sleeping = dual.EstadoFisiologico == "dormindo";
            // Mesmo dormindo, a emoção lida pelo classificador é mantida como secundária.
            string? secondary = dual.EstadoEmocional ?? (sleeping ? "neutro" : null);
            var decision = new EmotionDecision
            {
                EstadoPrincipal = dual.EstadoFisiologico,
                EstadoFisiologico = dual.EstadoFisiologico,
                ConfiancaFisiologica = dual.ConfiancaFisiologica,
                EstadoEmocional = secondary,
                ConfiancaEmocional = secondary == null ? null : dual.ConfiancaEmocional,
                EmocaoSecundaria = secondary,
                Fonte = "classificador",
                Observacoes = new[] { "Pipeline de landmarks indisponível; resultado do classificador ONNX." }
            };

            return new DetectionOutcome(
                decision,
                includeProbabilities ? _classifier.GetAllProbabilities(normalized) : null,
                facesDetected);
        }

        private IReadOnlyDictionary<string, float>? TryClassifierProbabilities(string base64Image)
        {
            try
            {
                return _classifier.GetAllProbabilities(_preprocessing.PreprocessBase64Image(base64Image));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível calcular as probabilidades do classificador.");
                return null;
            }
        }

        private static EmotionDecision AppendObservation(EmotionDecision decision, string note) => new()
        {
            EstadoPrincipal = decision.EstadoPrincipal,
            EmocaoSecundaria = decision.EmocaoSecundaria,
            EstadoFisiologico = decision.EstadoFisiologico,
            ConfiancaFisiologica = decision.ConfiancaFisiologica,
            EstadoEmocional = decision.EstadoEmocional,
            ConfiancaEmocional = decision.ConfiancaEmocional,
            Fonte = decision.Fonte,
            Metricas = decision.Metricas,
            Regras = decision.Regras,
            Observacoes = decision.Observacoes.Append(note).ToArray()
        };
    }
}
