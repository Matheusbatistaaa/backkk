using System;
using System.Collections.Generic;
using System.Linq;
using PerceptAI.API.ML;
using PerceptAI.API.Models;

namespace PerceptAI.API.Services
{
    /// <summary>
    /// Converte os 468 marcos faciais (e 21 pontos por mão) em métricas geométricas
    /// e fotométricas normalizadas. Nenhuma emoção é decidida aqui.
    /// </summary>
    public sealed class FacialMetricsService
    {
        private readonly EmotionRuleOptions _o;

        public FacialMetricsService(EmotionRuleOptions options) => _o = options;

        public FacialMetrics Compute(ImageRgb24 image, FaceLandmarks face, IReadOnlyList<HandLandmarks> hands)
        {
            var p = face.Points;
            if (p.Length < FaceMeshIndices.TotalPoints)
                throw new InvalidOperationException($"Face Mesh retornou {p.Length} pontos; esperado >= {FaceMeshIndices.TotalPoints}.");

            var notes = new List<string>();

            float iod = D(p, FaceMeshIndices.RightEyeOuter, FaceMeshIndices.LeftEyeOuter);
            float faceWidth = D(p, FaceMeshIndices.RightCheek, FaceMeshIndices.LeftCheek);
            float faceHeight = D(p, FaceMeshIndices.Forehead, FaceMeshIndices.Chin);
            if (iod < 1e-3f)
                throw new InvalidOperationException("Pontos dos olhos coincidentes; malha facial inválida.");

            float rightEar = EyeAspect(p, FaceMeshIndices.RightEyeOuter, FaceMeshIndices.RightEyeInner,
                FaceMeshIndices.RightEyeUpper, FaceMeshIndices.RightEyeLower,
                FaceMeshIndices.RightEyeUpper2, FaceMeshIndices.RightEyeLower2);
            float leftEar = EyeAspect(p, FaceMeshIndices.LeftEyeOuter, FaceMeshIndices.LeftEyeInner,
                FaceMeshIndices.LeftEyeUpper, FaceMeshIndices.LeftEyeLower,
                FaceMeshIndices.LeftEyeUpper2, FaceMeshIndices.LeftEyeLower2);
            float ear = (rightEar + leftEar) / 2f;

            float innerMouthWidth = D(p, FaceMeshIndices.MouthRightInner, FaceMeshIndices.MouthLeftInner);
            float mouthOpening = D(p, FaceMeshIndices.UpperLipInner, FaceMeshIndices.LowerLipInner);
            float mouthWidth = D(p, FaceMeshIndices.MouthRightCorner, FaceMeshIndices.MouthLeftCorner);

            float mar = innerMouthWidth > 1e-3f ? mouthOpening / innerMouthWidth : 0f;
            float openRatio = faceHeight > 1e-3f ? mouthOpening / faceHeight : 0f;

            float cornerSag = 0f;
            if (mouthWidth > 1e-3f)
            {
                // Referência na comissura interna (canto interno dos lábios): ela fica
                // presa à "linha da boca" e NÃO desce quando a mandíbula abre, então
                // bocejo/grito não viram falsa tristeza. Cantos externos abaixo da
                // comissura => positivo; acima (sorriso) => negativo.
                float cornerY = (p[FaceMeshIndices.MouthRightCorner].Y + p[FaceMeshIndices.MouthLeftCorner].Y) / 2f;
                float innerCornerY = (p[FaceMeshIndices.MouthRightInner].Y + p[FaceMeshIndices.MouthLeftInner].Y) / 2f;
                // Eixo Y cresce para baixo: cantos abaixo da comissura => positivo.
                cornerSag = (cornerY - innerCornerY) / mouthWidth;
            }

            // Referência na linha dos quatro cantos dos olhos: estável à pálpebra,
            // diferente de usar o ponto superior do olho (que sobe em olhos arregalados).
            float eyeLineY = (p[FaceMeshIndices.RightEyeOuter].Y + p[FaceMeshIndices.RightEyeInner].Y
                            + p[FaceMeshIndices.LeftEyeOuter].Y + p[FaceMeshIndices.LeftEyeInner].Y) / 4f;
            float browY = (p[FaceMeshIndices.RightBrowInner].Y + p[FaceMeshIndices.LeftBrowInner].Y) / 2f;
            float browRaise = (eyeLineY - browY) / iod;

            var (teethRatio, teethPixels, mouthPixels) = AnalyzeTeeth(image, p, notes);
            var (handPoints, handDistance, handDetected) = AnalyzeHands(p, hands, mouthWidth, iod);

            // Estados de olhos exigem concordância dos dois olhos: piscar unilateral é ambíguo.
            bool rightClosed = rightEar < _o.EyeClosedEar;
            bool leftClosed = leftEar < _o.EyeClosedEar;
            // Fechamento real (dormindo): média baixa e os dois olhos longe do estado aberto.
            bool eyesClosed = ear < _o.EyeClosedEar
                              && rightEar < _o.EyeClosedMaxEar
                              && leftEar < _o.EyeClosedMaxEar;
            bool eyesAlmostClosed = !eyesClosed
                                    && ear < _o.EyeClosedSoftEar
                                    && rightEar < _o.EyeClosedSoftMaxEar
                                    && leftEar < _o.EyeClosedSoftMaxEar;

            bool rightNarrow = rightEar < _o.EyeNarrowEar;
            bool leftNarrow = leftEar < _o.EyeNarrowEar;
            bool eyesNarrowed = !rightClosed && !leftClosed && rightNarrow && leftNarrow;
            bool eyesOpen = !rightClosed && !leftClosed && !rightNarrow && !leftNarrow;
            // Arregalar exige média acima do limiar e nenhum olho em faixa fechada/ruidosa.
            bool eyesWide = !rightClosed && !leftClosed
                            && ear >= _o.WideEyeEar
                            && rightEar >= _o.MinWideEyeSideEar
                            && leftEar >= _o.MinWideEyeSideEar;

            if (rightClosed != leftClosed)
                notes.Add($"Olhos assimétricos (direito={rightEar:F3}, esquerdo={leftEar:F3}); estado de vigília indeterminado.");

            bool yawning = mar >= _o.YawnMouthAspectRatio && openRatio >= _o.YawnOpenRatio;
            // A referência na comissura interna é estável à abertura da boca, então o
            // frown não precisa mais de trava por MAR (bocejo/grito não o disparam).
            bool frowning = cornerSag >= _o.FrownSagRatio;
            // Cantos acima da comissura: sorriso. Impede que sorriso com dentes vire "dor".
            bool smiling = cornerSag <= -_o.SmileCornerLiftRatio;
            bool browsRaised = browRaise >= _o.BrowRaiseRatio;
            bool teethVisible = teethRatio >= _o.TeethPixelRatio && teethPixels >= _o.MinTeethPixels;
            bool teethClenched = teethVisible && mar <= _o.TeethMaxMouthAspectRatio;
            bool handOnMouth = handPoints >= _o.MinHandPointsInMouthRegion
                               && handDistance <= _o.MaxHandMouthDistanceRatio;

            if (faceWidth < _o.MinFaceWidthPx)
                notes.Add($"Rosto pequeno ({faceWidth:F0}px < {_o.MinFaceWidthPx:F0}px); métricas menos confiáveis.");

            return new FacialMetrics
            {
                InterocularDistance = iod,
                FaceWidth = faceWidth,
                FaceHeight = faceHeight,
                EyeAspectRight = rightEar,
                EyeAspectLeft = leftEar,
                EyeAspectRatio = ear,
                MouthAspectRatio = mar,
                MouthOpenRatio = openRatio,
                MouthOpeningPx = mouthOpening,
                MouthWidth = mouthWidth,
                MouthCornerSagRatio = cornerSag,
                BrowRaiseRatio = browRaise,
                TeethRatio = teethRatio,
                TeethPixels = teethPixels,
                MouthRegionPixels = mouthPixels,
                HandPointsInMouthRegion = handPoints,
                HandMouthDistanceRatio = handDistance,
                HandDetected = handDetected,
                RightEyeClosed = rightClosed,
                LeftEyeClosed = leftClosed,
                EyesClosed = eyesClosed,
                EyesAlmostClosed = eyesAlmostClosed,
                EyesNarrowed = eyesNarrowed,
                EyesOpen = eyesOpen,
                EyesWide = eyesWide,
                Yawning = yawning,
                Frowning = frowning,
                Smiling = smiling,
                BrowsRaised = browsRaised,
                TeethVisible = teethVisible,
                TeethClenched = teethClenched,
                HandOnMouth = handOnMouth,
                Notes = notes
            };
        }

        private static float D(Point3[] p, int a, int b) => DetectionMath.Distance(p[a], p[b]);

        private static float EyeAspect(Point3[] p, int outer, int inner, int upper, int lower, int upper2, int lower2)
        {
            float width = DetectionMath.Distance(p[outer], p[inner]);
            if (width < 1e-3f) return 0f;
            float v1 = DetectionMath.Distance(p[upper], p[lower]);
            float v2 = DetectionMath.Distance(p[upper2], p[lower2]);
            return (v1 + v2) / (2f * width);
        }

        /// <summary>
        /// Mede a fração de pixels claros/pouco saturados dentro do contorno interno dos lábios.
        /// Dentes aparecem como banda clara; gengiva, lábios e cavidade escura não contam.
        /// </summary>
        private static (float Ratio, int TeethPixels, int RegionPixels) AnalyzeTeeth(
            ImageRgb24 image, Point3[] p, List<string> notes)
        {
            var contour = FaceMeshIndices.InnerLipContour
                .Select(i => new Point2(p[i].X, p[i].Y))
                .ToList();
            var hull = ConvexHull(contour);
            if (hull.Count < 3)
            {
                notes.Add("Contorno interno da boca degenerado.");
                return (0f, 0, 0);
            }

            float x1 = hull.Min(v => v.X), x2 = hull.Max(v => v.X);
            float y1 = hull.Min(v => v.Y), y2 = hull.Max(v => v.Y);
            int left = Math.Max(0, (int)MathF.Floor(x1));
            int right = Math.Min(image.Width - 1, (int)MathF.Ceiling(x2));
            int top = Math.Max(0, (int)MathF.Floor(y1));
            int bottom = Math.Min(image.Height - 1, (int)MathF.Ceiling(y2));

            int region = 0, teeth = 0;
            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    if (!PointInConvexPolygon(hull, x + 0.5f, y + 0.5f)) continue;
                    region++;

                    var (r, g, b) = image.GetPixel(x, y);
                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));
                    float saturation = max > 0 ? (max - min) / (float)max : 0f;

                    if (max >= 130 && saturation <= 0.35f) teeth++;
                }
            }

            if (region < 30)
            {
                notes.Add("Região da boca pequena para análise de dentes.");
                return (0f, teeth, region);
            }

            return (teeth / (float)region, teeth, region);
        }

        private (int Points, float DistanceRatio, bool Detected) AnalyzeHands(
            Point3[] p, IReadOnlyList<HandLandmarks> hands, float mouthWidth, float iod)
        {
            if (hands.Count == 0 || mouthWidth < 1e-3f)
                return (0, 0f, false);

            float cx = (p[FaceMeshIndices.UpperLipInner].X + p[FaceMeshIndices.LowerLipInner].X) / 2f;
            float cy = (p[FaceMeshIndices.UpperLipInner].Y + p[FaceMeshIndices.LowerLipInner].Y) / 2f;
            float halfW = _o.HandRegionWidthFactor * mouthWidth;
            float halfH = _o.HandRegionHeightFactor * mouthWidth;

            int bestPoints = 0;
            float bestDistance = float.MaxValue;

            foreach (var hand in hands)
            {
                int inside = 0;
                float sumX = 0f, sumY = 0f;
                foreach (var point in hand.Points)
                {
                    sumX += point.X;
                    sumY += point.Y;
                    if (MathF.Abs(point.X - cx) <= halfW && MathF.Abs(point.Y - cy) <= halfH) inside++;
                }

                if (hand.Points.Length > 0)
                {
                    float hx = sumX / hand.Points.Length;
                    float hy = sumY / hand.Points.Length;
                    float distance = MathF.Sqrt((hx - cx) * (hx - cx) + (hy - cy) * (hy - cy)) / iod;
                    bestDistance = MathF.Min(bestDistance, distance);
                }

                bestPoints = Math.Max(bestPoints, inside);
            }

            return (bestPoints, float.IsFinite(bestDistance) ? bestDistance : 0f, true);
        }

        // ---- geometria auxiliar ----

        private static List<Point2> ConvexHull(List<Point2> points)
        {
            var sorted = points.OrderBy(v => v.X).ThenBy(v => v.Y).ToList();
            if (sorted.Count <= 1) return sorted;

            var hull = new List<Point2>();
            foreach (var point in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }

            int lower = hull.Count + 1;
            for (int i = sorted.Count - 2; i >= 0; i--)
            {
                var point = sorted[i];
                while (hull.Count >= lower && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }

            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static float Cross(Point2 o, Point2 a, Point2 b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        private static bool PointInConvexPolygon(List<Point2> polygon, float px, float py)
        {
            bool hasNegative = false, hasPositive = false;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                float cross = (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);
                if (cross > 0f) hasPositive = true;
                else if (cross < 0f) hasNegative = true;
                if (hasPositive && hasNegative) return false;
            }
            return true;
        }
    }
}
