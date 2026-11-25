using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using PerceptAI.API.Services;

namespace PerceptAI.API.Controllers
{
    public sealed class NovoPacienteRequest
    {
        public string? Nome { get; set; }
        public string? Cpf { get; set; }
        public string? DataNascimento { get; set; }
        public string? Convenio { get; set; }
        public string? Diagnostico { get; set; }
    }

    [ApiController]
    [Route("api/cadastro")]
    public class CadastroController : ControllerBase
    {
        private readonly LocalCadastroService _cadastro;

        public CadastroController(LocalCadastroService cadastro)
        {
            _cadastro = cadastro;
        }

        [HttpGet("leitos")]
        public IActionResult GetLeitos() => Ok(_cadastro.GetLeitos());

        [HttpGet("pacientes")]
        public IActionResult GetPacientes() => Ok(_cadastro.GetPacientes());

        [HttpPost("pacientes")]
        public Task<IActionResult> PostPaciente([FromBody] NovoPacienteRequest body)
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Nome)
                || string.IsNullOrWhiteSpace(body.Cpf)
                || string.IsNullOrWhiteSpace(body.DataNascimento))
            {
                return Task.FromResult<IActionResult>(
                    BadRequest(new { message = "nome, cpf e dataNascimento sao obrigatorios" }));
            }

            var paciente = _cadastro.AddPaciente(
                body.Nome!, body.Cpf!, body.DataNascimento!, body.Convenio, body.Diagnostico);

            return Task.FromResult<IActionResult>(Ok(paciente));
        }
    }
}
