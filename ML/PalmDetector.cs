using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using PerceptAI.API.Models;

namespace PerceptAI.API.ML
{
    /// <summary>Palma detectada: caixa quadrada, punho e MCP do dedo médio (em pixels).</summary>
    public sealed class PalmDetection
    {
        public float Score { get; init; }
        public float CenterX { get; init; }
        public float CenterY { get; init; }
        public float Width { get; init; }
        public Point2 Wrist { get; init; }
        public Point2 MiddleMcp { get; init; }
    }

    /// <summary>
    /// BlazePalm full (192x192, letterbox, RGB [0,1]) com decode SSD e NMS ponderado.
    /// </summary>
    public sealed class PalmDetector
    {
        public const int InputSize = 192;
        private const int RowWidth = 9;

        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly Point2[] _anchors;

        public PalmDetector(InferenceSession session)
        {
            _session = session;
            _inputName = session.InputMetadata.Keys.First();
            _anchors = DetectionMath.GenerateAnchors(InputSize, new[] { 8, 16, 16, 16 });
        }

        public IReadOnlyList<PalmDetection> Detect(ImageRgb24 image, float threshold = 0.5f)
        {
            var canvas = ImageWarp.Letterbox(image, InputSize, out float scale, out float padX, out float padY);
            var tensor = TensorBuilder.UnitChw(canvas);

            using var results = _session.Run(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
            var regressors = results.First(r => r.Name == "regressors").AsTensor<float>().ToArray();
            var scoreLogits = results.First(r => r.Name == "classificators").AsTensor<float>().ToArray();

            var rows = new List<float[]>();
            for (int i = 0; i < scoreLogits.Length; i++)
            {
                float score = DetectionMath.Sigmoid(scoreLogits[i]);
                if (score < threshold) continue;

                int r = i * 18;
                var anchor = _anchors[i];
                var row = new float[RowWidth];
                row[0] = score;
                row[1] = regressors[r] / InputSize + anchor.X;
                row[2] = regressors[r + 1] / InputSize + anchor.Y;
                row[3] = regressors[r + 2] / InputSize;
                row[4] = regressors[r + 3] / InputSize;
                row[5] = regressors[r + 4] / InputSize + anchor.X;
                row[6] = regressors[r + 5] / InputSize + anchor.Y;
                row[7] = regressors[r + 8] / InputSize + anchor.X;
                row[8] = regressors[r + 9] / InputSize + anchor.Y;
                if (!(row[3] > 0f) || !(row[4] > 0f)) continue;
                rows.Add(row);
            }

            if (rows.Count == 0) return Array.Empty<PalmDetection>();

            var kept = DetectionMath.WeightedNms(rows);
            var palms = new List<PalmDetection>(kept.Count);

            foreach (var row in kept)
            {
                var center = Unletterbox(row[1], row[2], scale, padX, padY);
                var wrist = Unletterbox(row[5], row[6], scale, padX, padY);
                var mcp = Unletterbox(row[7], row[8], scale, padX, padY);
                float width = MathF.Max(row[3], row[4]) * InputSize / scale;

                palms.Add(new PalmDetection
                {
                    Score = row[0],
                    CenterX = center.X,
                    CenterY = center.Y,
                    Width = width,
                    Wrist = wrist,
                    MiddleMcp = mcp
                });
            }

            return palms;
        }

        private static Point2 Unletterbox(float x, float y, float scale, float padX, float padY)
            => new((x * InputSize - padX) / scale, (y * InputSize - padY) / scale);
    }
}
