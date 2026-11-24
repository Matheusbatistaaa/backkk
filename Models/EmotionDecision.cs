using System.Collections.Generic;

namespace PerceptAI.API.Models
{
    
    public sealed record RuleEvaluation(
        string Emotion,
        bool Matched,
        float Confidence,
        IReadOnlyList<string> Evidence);

    public sealed class EmotionDecision
    {
        public string EstadoFisiologico { get; init; } = "acordado";
        public float ConfiancaFisiologica { get; init; }
        public string? EstadoEmocional { get; init; }
        public float? ConfiancaEmocional { get; init; }

        public string Fonte { get; init; } = "landmarks";

        public FacialMetrics? Metricas { get; init; }
        public IReadOnlyList<RuleEvaluation> Regras { get; init; } = System.Array.Empty<RuleEvaluation>();
        public IReadOnlyList<string> Observacoes { get; init; } = System.Array.Empty<string>();
    }
}
