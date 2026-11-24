using Microsoft.ML.OnnxRuntime;
using System.IO;

namespace PerceptAI.API.ML
{
    /// <summary>
    /// Ponto único de carregamento das sessões ONNX.
    /// Os quatro modelos do pipeline de marcos são opcionais: se algum arquivo faltar,
    /// <see cref="HasLandmarkPipeline"/> fica falso e a API continua com o classificador.
    /// </summary>
    public class ModelLoader
    {
        public InferenceSession PhysioSession { get; }
        public InferenceSession EmotionSession { get; }

        public BlazeFaceDetector? FaceDetector { get; }
        public FaceMeshPredictor? FaceMesh { get; }
        public PalmDetector? PalmDetector { get; }
        public HandLandmarkPredictor? HandLandmarks { get; }

        public bool HasLandmarkPipeline =>
            FaceDetector != null && FaceMesh != null && PalmDetector != null && HandLandmarks != null;

        public ModelLoader(string physioModelPath, string emotionModelPath)
        {
            PhysioSession = LoadRequired(physioModelPath, "fisiológico");
            EmotionSession = LoadRequired(emotionModelPath, "emocional");
        }

        public ModelLoader(string physioModelPath, string emotionModelPath, string? landmarkDirectory)
            : this(physioModelPath, emotionModelPath)
        {
            if (string.IsNullOrWhiteSpace(landmarkDirectory))
                return;

            var faceSession = LoadOptional(Path.Combine(landmarkDirectory, "face_detection_short_range.onnx"));
            var meshSession = LoadOptional(Path.Combine(landmarkDirectory, "face_mesh_Nx3x192x192.onnx"));
            var palmSession = LoadOptional(Path.Combine(landmarkDirectory, "palm_detection_full.onnx"));
            var handSession = LoadOptional(Path.Combine(landmarkDirectory, "hand_landmark_full.onnx"));

            if (faceSession != null) FaceDetector = new BlazeFaceDetector(faceSession);
            if (meshSession != null) FaceMesh = new FaceMeshPredictor(meshSession);
            if (palmSession != null) PalmDetector = new PalmDetector(palmSession);
            if (handSession != null) HandLandmarks = new HandLandmarkPredictor(handSession);
        }

        // Construtor legado: um único arquivo usado para os dois classificadores.
        public ModelLoader(string modelPath)
        {
            PhysioSession = new InferenceSession(modelPath);
            EmotionSession = new InferenceSession(modelPath);
        }

        private static InferenceSession LoadRequired(string path, string description)
            => File.Exists(path)
                ? new InferenceSession(path)
                : throw new FileNotFoundException($"Modelo {description} não encontrado em '{path}'.");

        private static InferenceSession? LoadOptional(string path)
            => File.Exists(path) ? new InferenceSession(path) : null;
    }
}
