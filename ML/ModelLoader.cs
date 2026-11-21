using Microsoft.ML.OnnxRuntime;
using System.IO;

namespace PerceptAI.API.ML
{
    public class ModelLoader
    {
        public InferenceSession PhysioSession { get; }
        public InferenceSession EmotionSession { get; }

        public ModelLoader(string physioModelPath, string emotionModelPath)
        {
            PhysioSession = File.Exists(physioModelPath)
                ? new InferenceSession(physioModelPath)
                : throw new FileNotFoundException($"Physio model not found at '{physioModelPath}'.");

            EmotionSession = File.Exists(emotionModelPath)
                ? new InferenceSession(emotionModelPath)
                : throw new FileNotFoundException($"Emotion model not found at '{emotionModelPath}'.");
        }

        // Legacy constructor for backwards compatibility
        public ModelLoader(string modelPath)
        {
            PhysioSession = new InferenceSession(modelPath);
            EmotionSession = new InferenceSession(modelPath);
        }
    }
}