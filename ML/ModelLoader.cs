using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.ML.OnnxRuntime;

namespace PerceptAI.API.ML
{
    /// <summary>
    /// Carrega as sessões ONNX do pipeline. O extrator de landmarks é opcional:
    /// se algum modelo faltar, o serviço cai para o classificador legado,
    /// evitando que a API fique indisponível.
    /// </summary>
    public sealed class ModelLoader
    {
        public InferenceSession? EmotionClassifier { get; }
        public BlazeFaceDetector? FaceDetector { get; }
        public FaceMeshPredictor? FaceMesh { get; }
        public PalmDetector? PalmDetector { get; }
        public HandLandmarkPredictor? HandLandmarks { get; }

        public bool HasLandmarkPipeline =>
            FaceDetector != null && FaceMesh != null && PalmDetector != null && HandLandmarks != null;

        public bool HasClassifier => EmotionClassifier != null;

        public IReadOnlyList<string> LoadWarnings { get; }

        public ModelLoader(string? classifierPath, string? faceDetectorPath, string? faceMeshPath,
            string? palmPath, string? handLandmarkPath)
        {
            var missing = new List<string>();

            EmotionClassifier = TryLoad(classifierPath, "classificador de emoções", missing);
            var faceDet = TryLoad(faceDetectorPath, "detector de rostos", missing);
            var mesh = TryLoad(faceMeshPath, "face mesh", missing);
            var palm = TryLoad(palmPath, "detector de palmas", missing);
            var hand = TryLoad(handLandmarkPath, "landmarks de mão", missing);

            FaceDetector = faceDet != null ? new BlazeFaceDetector(faceDet) : null;
            FaceMesh = mesh != null ? new FaceMeshPredictor(mesh) : null;
            PalmDetector = palm != null ? new PalmDetector(palm) : null;
            HandLandmarks = hand != null ? new HandLandmarkPredictor(hand) : null;

            if (EmotionClassifier == null && !HasLandmarkPipeline)
                throw new FileNotFoundException(
                    "Nenhum modelo ONNX utilizável encontrado em ML/. " +
                    "Esperados: perceptai_mobilenetv2.onnx (+.data), face_detection_short_range.onnx, " +
                    "face_mesh_Nx3x192x192.onnx, palm_detection_full.onnx, hand_landmark_full.onnx.");

            LoadWarnings = missing;
        }

        private static InferenceSession? TryLoad(string? path, string label, List<string> missing)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                missing.Add(label);
                return null;
            }
            return new InferenceSession(path);
        }
    }
}
