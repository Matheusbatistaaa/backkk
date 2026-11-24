using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PerceptAI.API.Models;
using PerceptAI.API.Services;

namespace PerceptAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DetectionController : ControllerBase
    {
        private readonly DetectionPipelineService _pipeline;
        private readonly SupabaseService _supabaseService;
        private readonly ILogger<DetectionController> _logger;
        private readonly double _threshold;

        public DetectionController(
            DetectionPipelineService pipeline,
            SupabaseService supabaseService,
            ILogger<DetectionController> logger,
            IConfiguration configuration)
        {
            _pipeline = pipeline;
            _supabaseService = supabaseService;
            _logger = logger;
            _threshold = configuration.GetValue<double>("DetectionSettings:Threshold", 0.75);
        }

        /// <summary>
        /// Health check usado pelos scripts de inicialização, pelo túnel e pelo Render.
        /// Responde rápido sem inferência.
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health() => Ok(new
        {
            status = "ok",
            pipeline = _pipeline.PipelineName,
            landmarksAvailable = _pipeline.LandmarksAvailable,
            timestamp = DateTime.UtcNow
        });

        /// <summary>
        /// Recebe uma imagem Base64, executa o pipeline de detecção e persiste o resultado.
        /// </summary>
        [HttpPost("detect")]
        public async Task<IActionResult> Detect([FromBody] DetectionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Image))
            {
                return BadRequest(new { message = "Corpo de requisição inválido ou imagem vazia." });
            }

            bool temPatient = !string.IsNullOrWhiteSpace(request.PatientId);
            bool temInternacao = !string.IsNullOrWhiteSpace(request.InternacaoId);

            if (!temPatient && !temInternacao)
            {
                return BadRequest(new { message = "PatientId ou InternacaoId é obrigatório." });
            }

            try
            {
                DetectionOutcome outcome = _pipeline.Analyze(request.Image);
                EmotionDecision decision = outcome.Decision;

                bool indeterminate = decision.EstadoPrincipal == "indeterminado";
                bool sleeping = decision.EstadoPrincipal == "dormindo";

                // O contrato legado (Emotion/Confidence) continua refletindo o estado que
                // prevalece: dormindo vence mesmo com emoção secundária presente.
                string mood = indeterminate
                    ? "indeterminado"
                    : sleeping
                        ? "dormindo"
                        : decision.EstadoEmocional ?? "neutro";
                float confianca = indeterminate || sleeping
                    ? decision.ConfiancaFisiologica
                    : decision.ConfiancaEmocional ?? 0f;

                var response = new DetectionResponse
                {
                    Emotion = mood,
                    Confidence = confianca,
                    Timestamp = DateTime.UtcNow,
                    EstadoPrincipal = decision.EstadoPrincipal,
                    EmocaoSecundaria = decision.EstadoEmocional,
                    EstadoFisiologico = decision.EstadoFisiologico,
                    ConfiancaFisiologica = decision.ConfiancaFisiologica,
                    EstadoEmocional = decision.EstadoEmocional,
                    ConfiancaEmocional = decision.ConfiancaEmocional,
                    Fonte = decision.Fonte,
                    FacesDetectadas = outcome.FacesDetected,
                    Observacoes = decision.Observacoes,
                    Regras = decision.Regras,
                    Metricas = decision.Metricas
                };

                // Sem rosto/estado indeterminado: nada é persistido.
                if (indeterminate)
                    return Ok(response);

                if (temInternacao)
                {
                    await _supabaseService.SaveExpressaoAsync(request.InternacaoId!, mood, confianca);
                }
                else if (temPatient && confianca >= _threshold)
                {
                    await _supabaseService.SaveDetectionAsync(request.PatientId, mood, confianca, response.Timestamp);
                }

                return Ok(response);
            }
            catch (FormatException)
            {
                return BadRequest(new { message = "Formato/imagem Base64 inválido." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro interno ao processar a detecção.");
                return StatusCode(500, new { message = "Erro interno ao processar a detecção." });
            }
        }
    }
}
