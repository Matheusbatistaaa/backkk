using System.Collections.Generic;

namespace PerceptAI.API.Models
{
    /// <summary>Avaliação de uma regra de emoção (mesma estrutura para todas as classes).</summary>
    public sealed record RuleEvaluation(
        string Emotion,
        bool Matched,
        float Confidence,
        IReadOnlyList<string> Evidence);

    /// <summary>Resultado único e padronizado do pipeline de análise.</summary>
    public sealed class EmotionDecision
    {
        /// <summary>Estado que prevalece: "dormindo", "acordado" ou "indeterminado".</summary>
        public string EstadoPrincipal { get; init; } = "acordado";

        /// <summary>Emoção devolvida como dado secundário (não prevalece sobre "dormindo").</summary>
        public string? EmocaoSecundaria { get; init; }

        public string EstadoFisiologico { get; init; } = "acordado";
        public float ConfiancaFisiologica { get; init; }
        public string? EstadoEmocional { get; init; }
        public float? ConfiancaEmocional { get; init; }

        /// <summary>"landmarks" (regras geométricas) ou "classificador" (fallback).</summary>
        public string Fonte { get; init; } = "landmarks";

        public FacialMetrics? Metricas { get; init; }
        public IReadOnlyList<RuleEvaluation> Regras { get; init; } = System.Array.Empty<RuleEvaluation>();
        public IReadOnlyList<string> Observacoes { get; init; } = System.Array.Empty<string>();
    }
}
