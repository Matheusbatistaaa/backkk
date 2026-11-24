using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PerceptAI.API.Models;

namespace PerceptAI.API.Models
{
    /// <summary>
    /// Resposta do endpoint /api/detection/detect.
    /// Os três primeiros campos mantêm o contrato legado (PascalCase) usado pelo front.
    /// Os demais expõem o resultado detalhado e auditável do pipeline.
    /// </summary>
    public class DetectionResponse
    {
        [JsonPropertyName("Emotion")]
        public string Emotion { get; set; } = string.Empty;

        [JsonPropertyName("Confidence")]
        public float Confidence { get; set; }

        [JsonPropertyName("Timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("estadoPrincipal")]
        public string EstadoPrincipal { get; set; } = "acordado";

        /// <summary>Emoção secundária devolvida mesmo quando o estado principal é "dormindo".</summary>
        [JsonPropertyName("emocaoSecundaria")]
        public string? EmocaoSecundaria { get; set; }

        [JsonPropertyName("estadoFisiologico")]
        public string EstadoFisiologico { get; set; } = "acordado";

        [JsonPropertyName("confiancaFisiologica")]
        public float ConfiancaFisiologica { get; set; }

        [JsonPropertyName("estadoEmocional")]
        public string? EstadoEmocional { get; set; }

        [JsonPropertyName("confiancaEmocional")]
        public float? ConfiancaEmocional { get; set; }

        /// <summary>"landmarks" (regras geométricas) ou "classificador" (fallback).</summary>
        [JsonPropertyName("fonte")]
        public string Fonte { get; set; } = "landmarks";

        [JsonPropertyName("facesDetectadas")]
        public int FacesDetectadas { get; set; }

        [JsonPropertyName("observacoes")]
        public IReadOnlyList<string> Observacoes { get; set; } = Array.Empty<string>();

        [JsonPropertyName("regras")]
        public IReadOnlyList<RuleEvaluation> Regras { get; set; } = Array.Empty<RuleEvaluation>();

        [JsonPropertyName("metricas")]
        public FacialMetrics? Metricas { get; set; }
    }
}
