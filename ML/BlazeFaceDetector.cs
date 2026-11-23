using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PerceptAI.API.Models;

namespace PerceptAI.API.ML
{
    /// <summary>
    /// BlazeFace short-range (entrada 128x128 em [-1,1]) com decode SSD e NMS ponderado.
    /// </summary>
    public sealed class BlazeFaceDetector
    {
        public const int InputSize = 128;
        private const int NumKeypoints = 6;
        private const int RegressorStride = 4 + 2 * NumKeypoints; // 16 valores por âncora
        private const int RowWidth = 5 + 2 * NumKeypoints;        // score + caixa + keypoints

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly Point2[] _anchors;

        public BlazeFaceDetector(InferenceSession session)
        {
            _session = session;
            _inputName = session.InputMetadata.Keys.First();
            _anchors = DetectionMath.GenerateAnchors(InputSize, new[] { 8, 16, 16, 16 });
        }

        public IReadOnlyList<FaceDetection> Detect(ImageRgb24 image, float threshold = 0.5f)
        {
            var canvas = ImageWarp.Letterbox(image, InputSize, out float scale, out float padX, out float padY);
            var tensor = TensorBuilder.SignedChw(canvas);

            using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
            var regressors = GetValues(results, "regressors");
            var scoreLogits = GetValues(results, "scores");

            int count = scoreLogits.Length;
            var rows = new List<float[]>();

            for (int i = 0; i < count; i++)
            {
                float score = DetectionMath.Sigmoid(scoreLogits[i]);
                if (score < threshold) continue;

                int r = i * RegressorStride;
                var anchor = _anchors[i];
                var row = new float[RowWidth];
                row[0] = score;
                row[1] = regressors[r] / InputSize + anchor.X;
                row[2] = regressors[r + 1] / InputSize + anchor.Y;
                row[3] = regressors[r + 2] / InputSize;
                row[4] = regressors[r + 3] / InputSize;
                for (int k = 0; k < NumKeypoints; k++)
                {
                    row[5 + 2 * k] = regressors[r + 4 + 2 * k] / InputSize + anchor.X;
                    row[6 + 2 * k] = regressors[r + 5 + 2 * k] / InputSize + anchor.Y;
                }
                if (!(row[3] > 0f) || !(row[4] > 0f)) continue;
                rows.Add(row);
            }

            if (rows.Count == 0) return Array.Empty<FaceDetection>();

            var kept = DetectionMath.WeightedNms(rows);
            var detections = new List<FaceDetection>(kept.Count);

            foreach (var row in kept)
            {
                var center = Unletterbox(row[1], row[2], scale, padX, padY);
                float halfW = row[3] * InputSize / scale / 2f;
                float halfH = row[4] * InputSize / scale / 2f;

                var keypoints = new Point2[NumKeypoints];
                for (int k = 0; k < NumKeypoints; k++)
                    keypoints[k] = Unletterbox(row[5 + 2 * k], row[6 + 2 * k], scale, padX, padY);

                detections.Add(new FaceDetection
                {
                    Bounds = new RectF(center.X - halfW, center.Y - halfH, halfW * 2f, halfH * 2f),
                    Keypoints = keypoints,
                    Score = row[0]
                });
            }

            return detections;
        }

        private static Point2 Unletterbox(float x, float y, float scale, float padX, float padY)
            => new((x * InputSize - padX) / scale, (y * InputSize - padY) / scale);

        private static float[] GetValues(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
        {
            var value = results.FirstOrDefault(r => r.Name == name) ?? results.First();
            return value.AsTensor<float>().ToArray();
        }
    }
}
