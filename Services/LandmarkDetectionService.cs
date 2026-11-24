using System;
using Microsoft.Extensions.Logging;
using PerceptAI.API.ML;
using PerceptAI.API.Models;

namespace PerceptAI.API.Services
{
    
    public sealed class LandmarkDetectionService
    {
        private readonly ModelLoader _models;
        private readonly EmotionRuleOptions _options;
        private readonly ILogger<LandmarkDetectionService> _logger;

        public LandmarkDetectionService(ModelLoader models, EmotionRuleOptions options, ILogger<LandmarkDetectionService> logger)
        {
            _models = models;
            _options = options;
            _logger = logger;
        }

        public bool Available => _models.HasLandmarkPipeline;

        public LandmarkFrame Detect(ImageRgb24 image)
        {
            if (!Available)
                return new LandmarkFrame { Available = false };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var faces = _models.FaceDetector!.Detect(image, _options.MinDetectionScore);
            _logger.LogInformation("Timing: face detect {Ms} ms ({Faces} rostos)", sw.ElapsedMilliseconds, faces.Count);
            if (faces.Count == 0)
                return new LandmarkFrame { Available = true, FacesDetected = 0 };

            FaceDetection best = faces[0];
            foreach (var face in faces)
                if (face.Score > best.Score) best = face;

            sw.Restart();
            var meshOutput = _models.FaceMesh!.Predict(image, best);
            _logger.LogInformation("Timing: face mesh {Ms} ms (score={Score:F3}, caixa={W:F0}x{H:F0})",
                sw.ElapsedMilliseconds, meshOutput.Landmarks.Score,
                meshOutput.Landmarks.Bounds.Width, meshOutput.Landmarks.Bounds.Height);
            FaceLandmarks? landmarks = meshOutput.Landmarks.Score >= _options.MinFaceMeshScore
                ? meshOutput.Landmarks
                : null;

            if (landmarks == null)
                return new LandmarkFrame { Available = true, FacesDetected = faces.Count };

            sw.Restart();
            var palms = _models.PalmDetector!.Detect(image, _options.MinDetectionScore);
            _logger.LogInformation("Timing: palm detect {Ms} ms ({Palms} palmas)", sw.ElapsedMilliseconds, palms.Count);
            sw.Restart();
            var hands = _models.HandLandmarks!.Predict(image, palms, _options.MinDetectionScore);
            _logger.LogInformation("Timing: hand landmark {Ms} ms ({Hands} mãos)", sw.ElapsedMilliseconds, hands.Length);

            _logger.LogDebug("Landmarks: {Faces} rosto(s), mesh score={Mesh:F3}, {Hands} mão(s)",
                faces.Count, landmarks.Score, hands.Length);

            return new LandmarkFrame
            {
                Available = true,
                FacesDetected = faces.Count,
                Face = landmarks,
                Hands = hands
            };
        }
    }
}
