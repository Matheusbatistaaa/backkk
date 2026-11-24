using System;
using System.Collections.Generic;

namespace PerceptAI.API.Models
{
    public class DetectionResponse
    {
        
        public string Emotion { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public DateTime Timestamp { get; set; }

        public string? EstadoFisiologico { get; set; }
        public string? EstadoEmocional { get; set; }
        public float? ConfiancaFisiologica { get; set; }
        public float? ConfiancaEmocional { get; set; }
        public string? Fonte { get; set; }
        public FacialMetrics? Metricas { get; set; }
        public IReadOnlyList<RuleEvaluation>? Regras { get; set; }
        public IReadOnlyList<string>? Observacoes { get; set; }
    }
}
