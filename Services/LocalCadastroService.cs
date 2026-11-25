using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PerceptAI.API.Services
{
    public sealed class LocalLeito
    {
        public Guid Id { get; set; }
        public int Numero { get; set; }
        public string Status { get; set; } = "livre";
    }

    public sealed class LocalPaciente
    {
        public Guid Id { get; set; }
        public string Nome { get; set; } = "";
        public string Cpf { get; set; } = "";
        public string DataNascimento { get; set; } = "";
        public string? Convenio { get; set; }
        public string? Diagnostico { get; set; }
        public int? Leito { get; set; }
        public DateTime CriadoEm { get; set; }
    }

    /// <summary>
    /// Cadastro local de pacientes/leitos em arquivo JSON. Existe porque o Supabase
    /// está com chave inválida (gravações retornam 401) e o cadastro precisa
    /// funcionar no host local sem depender do banco externo.
    /// </summary>
    public sealed class LocalCadastroService
    {
        private sealed class Store
        {
            public List<LocalLeito> Leitos { get; set; } = new();
            public List<LocalPaciente> Pacientes { get; set; } = new();
        }

        private readonly object _lock = new();
        private readonly string _path;
        private Store _store;

        public LocalCadastroService()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "cadastro.json");
            _store = Load();
        }

        private Store Load()
        {
            if (File.Exists(_path))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<Store>(File.ReadAllText(_path));
                    if (loaded is not null) return loaded;
                }
                catch { }
            }

            var seeded = new Store
            {
                Leitos = Enumerable.Range(0, 12)
                    .Select(i => new LocalLeito { Id = Guid.NewGuid(), Numero = 401 + i, Status = "livre" })
                    .ToList()
            };
            Save(seeded);
            return seeded;
        }

        private void Save(Store store)
        {
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }

        public IReadOnlyList<LocalLeito> GetLeitos()
        {
            lock (_lock) return _store.Leitos.OrderBy(l => l.Numero).ToList();
        }

        public IReadOnlyList<LocalPaciente> GetPacientes()
        {
            lock (_lock) return _store.Pacientes.OrderByDescending(p => p.CriadoEm).ToList();
        }

        public LocalPaciente AddPaciente(string nome, string cpf, string dataNascimento,
                                         string? convenio, string? diagnostico)
        {
            lock (_lock)
            {
                var livres = _store.Leitos.Where(l => l.Status == "livre").ToList();
                LocalLeito? leito = livres.Count > 0
                    ? livres[Random.Shared.Next(livres.Count)]
                    : null;

                if (leito is not null) leito.Status = "ocupado";

                var paciente = new LocalPaciente
                {
                    Id = Guid.NewGuid(),
                    Nome = nome.Trim(),
                    Cpf = cpf.Trim(),
                    DataNascimento = dataNascimento.Trim(),
                    Convenio = string.IsNullOrWhiteSpace(convenio) ? null : convenio.Trim(),
                    Diagnostico = string.IsNullOrWhiteSpace(diagnostico) ? null : diagnostico.Trim(),
                    Leito = leito?.Numero,
                    CriadoEm = DateTime.UtcNow
                };

                _store.Pacientes.Add(paciente);
                Save(_store);
                return paciente;
            }
        }
    }
}
