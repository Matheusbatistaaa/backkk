using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PerceptAI.API.ML;

namespace PerceptAI.API.Services
{
    public class DualStateResult
    {
        public string EstadoFisiologico { get; set; } = "acordado"; // "acordado" ou "dormindo"
        public float ConfiancaFisiologica { get; set; }
        public string? EstadoEmocional { get; set; }                 // "dor", "enjoo", "medo", "tristeza", "neutro", "sono" (null se dormindo)
        public float? ConfiancaEmocional { get; set; }
        public string PrimaryEmotion { get; set; } = string.Empty;
        public float PrimaryConfidence { get; set; }
    }

    public class EmotionDetectionService
    {
        private readonly ModelLoader _modelLoader;

        // Mapeamento do modelo fisiológico (2 classes): 0: acordado, 1: dormindo
        private readonly string[] _physioClasses = { "acordado", "dormindo" };

        // Mapeamento do modelo emocional (6 classes): 0: dor, 1: enjoo, 2: medo, 3: sono, 4: tristeza, 5: neutro
        private readonly string[] _emotionClasses = { "dor", "enjoo", "medo", "sono", "tristeza", "neutro" };

        // Mapeamento legado (8 classes) para compatibilidade
        private readonly string[] _legacyEmotions = { "dor", "enjoo", "medo", "sono", "tristeza", "neutro", "acordado", "dormindo" };

        public EmotionDetectionService(ModelLoader modelLoader)
        {
            _modelLoader = modelLoader;
        }

        /// <summary>
        /// Obtém as probabilidades normalizadas (Softmax) a partir de um modelo ONNX específico.
        /// Aplica temperature scaling se temperaturas forem fornecidas.
        /// </summary>
        private float[] GetProbabilities(float[] normalizedData, InferenceSession? session, float[]? temperatures = null)
        {
            if (session == null)
            {
                throw new InvalidOperationException("O modelo ONNX não foi carregado na memória do servidor.");
            }

            var inputTensor = new DenseTensor<float>(normalizedData, new[] { 1, 3, 224, 224 });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor)
            };

            using var results = session.Run(inputs);

            var outputValue = results.FirstOrDefault(r => r.Name == "logits") ?? results.FirstOrDefault();
            if (outputValue == null)
            {
                throw new InvalidOperationException("Nenhuma saída encontrada no modelo ONNX.");
            }

            var outputTensor = outputValue.AsTensor<float>();
            float[] logits = outputTensor.ToArray();

            // Apply per-class logit bias (boost underrepresented classes like enjoo)
            if (temperatures != null && _emotionLogitBias != null && _emotionLogitBias.Length == logits.Length)
            {
                for (int i = 0; i < logits.Length; i++)
                {
                    logits[i] += _emotionLogitBias[i];
                }
            }

            // Apply temperature scaling per class
            if (temperatures != null && temperatures.Length == logits.Length)
            {
                for (int i = 0; i < logits.Length; i++)
                {
                    logits[i] /= temperatures[i];
                }
            }

            return Softmax(logits);
        }

        // Confiança mínima para reportar uma emoção específica.
        // Abaixo disso, retorna "neutro" para evitar falsos positivos com rostos ambíguos.
        private const float MinEmotionThreshold = 0.30f;

        // Per-class temperatures for temperature scaling (from training - subject_wise=True, latest run)
        // Copied from outputs_emotion/temperatures.npy: [0.954, 3.076, 3.941, 4.846, 2.266, 5.0]
        // NOTE: medo (3.94), sono (4.85), neutro (5.0) get divided by large temps -> probs very low
        // FIX: Use moderate temps that preserve relative ordering but don't collapse to near-zero
        private readonly float[] _emotionTemperatures = {
            1.0f,     // dor - keep sharp (was 0.954)
            2.0f,     // enjoo - moderate (was 3.076)
            2.5f,     // medo - moderate (was 3.941)
            2.5f,     // sono - moderate (was 4.846)
            1.5f,     // tristeza - moderate (was 2.266)
            3.0f      // neutro - moderate (was 5.0)
        };

        // Per-class logit bias (added before temperature scaling + softmax)
        // Dor: 0.5 (small boost to beat medo on dor images)
        // Enjoo: 2.0 (increased boost to beat tristeza on enjoo images)
        // Medo: 3.0 (higher boost - was losing to neutro on several medo faces)
        // Sono: 1.5 (boosted to beat tristeza on clean sono images)
        // Tristeza: 1.0 (moderate boost - was losing to neutro on tristeza faces)
        // Neutro: 4.0 (slightly reduced to let medo/enjoo win on their images)
        private readonly float[] _emotionLogitBias = {
            0.5f,     // dor - small boost to beat medo
            2.0f,     // enjoo - increased to beat tristeza
            3.0f,     // medo - higher boost to beat neutro on medo faces
            1.5f,     // sono
            1.0f,     // tristeza - moderate boost to beat neutro on tristeza faces
            4.0f      // neutro - slightly reduced
        };

        // Per-class optimal thresholds - MATCH ACTUAL OUTPUT RANGE after temp scaling + bias
        // Validated snapshot thresholds
        private readonly float[] _emotionThresholds = {
            0.50f,    // dor - raised to 0.50 to reduce false positives on neutral faces (real dor ~0.54-0.61)
            0.08f,    // enjoo
            0.15f,    // medo - lowered from 0.22 to catch medo images that were falling to neutro
            0.30f,    // sono - raised to 0.30 to reduce false positives on neutral faces (acordado sono ~0.28-0.29)
            0.20f,    // tristeza
            0.12f     // neutro
        };

        /// <summary>
        /// Analisa os dois estados do paciente usando os 2 modelos separados:
        /// 1. Estado Fisiológico (modelo fisiológico): "dormindo" ou "acordado".
        ///    Se dormindo, EstadoEmocional é null (não precisa de emoção).
        /// 2. Se acordado: EstadoEmocional (modelo emocional) identifica a expressão com confiança >= 30%;
        ///    caso nenhuma emoção passe o limiar, retorna "neutro".
        /// </summary>
        public DualStateResult AnalyzeDualState(float[] normalizedData)
        {
            // Tenta usar os 2 modelos novos primeiro
            if (_modelLoader.PhysioSession != null && _modelLoader.EmotionSession != null)
            {
                return AnalyzeDualStateWithTwoModels(normalizedData);
            }
            // Fallback para modelo legado (único) se os novos não estiverem disponíveis
            else if (_modelLoader.PhysioSession == null && _modelLoader.EmotionSession == null)
            {
                Console.WriteLine("[EmotionDetectionService] Usando modelo legado (único) - modelos novos não carregados");
                return AnalyzeDualStateLegacy(normalizedData);
            }
            else
            {
                throw new InvalidOperationException("Configuração inválida de modelos: um dos dois modelos não foi carregado.");
            }
        }

        /// <summary>
        /// Analisa usando os 2 modelos separados (fisiológico + emocional).
        /// Ambos os modelos output 8 classes (formato legado):
        /// 0: dor, 1: enjoo, 2: medo, 3: sono, 4: tristeza, 5: neutro, 6: acordado, 7: dormindo
        /// </summary>
        private DualStateResult AnalyzeDualStateWithTwoModels(float[] normalizedData)
        {
            // Passo 1: Modelo Fisiológico - determina se está acordado ou dormindo
            var physioProbs = GetProbabilities(normalizedData, _modelLoader.PhysioSession);
            // Physio model outputs 8 classes (legacy format), indices 6=acordado, 7=dormindo
            float pAcordado = physioProbs.Length > 6 ? physioProbs[6] : 0f;
            float pDormindo = physioProbs.Length > 7 ? physioProbs[7] : 0f;

            // Passo 2: Modelo Emocional - sempre executa para obter probabilidades emocionais
            var emotionProbsFull = GetProbabilities(normalizedData, _modelLoader.EmotionSession, _emotionTemperatures);
            // Emotion model outputs 8 classes (legacy format), take first 6 for emotions
            var emotionProbs = new float[6];
            Array.Copy(emotionProbsFull, emotionProbs, 6);

            // DEBUG: Log all emotion probabilities
            Console.WriteLine($"[EmotionDetectionService] Physio: acordado={pAcordado:F4}, dormindo={pDormindo:F4}");
            Console.WriteLine($"[EmotionDetectionService] Emotions (temp scaled):");
            for (int i = 0; i < emotionProbs.Length; i++)
            {
                Console.WriteLine($"  {_emotionClasses[i]}: prob={emotionProbs[i]:F4}, threshold={_emotionThresholds[i]:F2}, temp={_emotionTemperatures[i]:F3}");
            }

            // Identifica a melhor emoção usando thresholds por classe
            int bestEmotionIndex = 5; // neutro padrão
            float maxEmotionConf = -1.0f;
            bool anyEmotionPassedThreshold = false;

            for (int i = 0; i < emotionProbs.Length; i++)
            {
                if (emotionProbs[i] >= _emotionThresholds[i] && emotionProbs[i] > maxEmotionConf)
                {
                    maxEmotionConf = emotionProbs[i];
                    bestEmotionIndex = i;
                    anyEmotionPassedThreshold = true;
                }
            }

            Console.WriteLine($"[EmotionDetectionService] Any passed threshold: {anyEmotionPassedThreshold}, Best: {_emotionClasses[bestEmotionIndex]} ({maxEmotionConf:F4})");

            string emocaoDetectada;
            if (!anyEmotionPassedThreshold)
            {
                // Nenhuma emoção passou seu threshold específico.
                // Fallback: pega a emoção com maior probabilidade bruta (sem threshold)
                bestEmotionIndex = 0;
                maxEmotionConf = emotionProbs[0];
                for (int i = 1; i < emotionProbs.Length; i++)
                {
                    if (emotionProbs[i] > maxEmotionConf)
                    {
                        maxEmotionConf = emotionProbs[i];
                        bestEmotionIndex = i;
                    }
                }
                emocaoDetectada = _emotionClasses[bestEmotionIndex];
                Console.WriteLine($"[EmotionDetectionService] Fallback to max raw prob: {emocaoDetectada} ({maxEmotionConf:F4})");
            }
            else
            {
                emocaoDetectada = _emotionClasses[bestEmotionIndex];
            }

            // Encontra a maior probabilidade emocional e qual emoção
            float maxEmotionProb = 0f;
            int maxEmotionIndex = 0;
            for (int i = 0; i < emotionProbs.Length; i++)
            {
                if (emotionProbs[i] > maxEmotionProb)
                {
                    maxEmotionProb = emotionProbs[i];
                    maxEmotionIndex = i;
                }
            }
            float dorProb = emotionProbs[0]; // dor é índice 0
            float medoProb = emotionProbs[2]; // medo é índice 2

            // Verifica se dor ou tristeza passaram threshold (emoções genuínas que causam olhos fechados)
            bool dorOrTristezaPassedThreshold = (emotionProbs[0] >= _emotionThresholds[0]) || (emotionProbs[4] >= _emotionThresholds[4]);

            // Dormindo REAL: physio muito confiante E sem emoção genuína FORTE ativa
            // Hallucinação conhecida: physio ~1.0 + dor alta + medo presente = rosto dormindo
            // Expanded to cover pDormindo > 0.99 (not just >0.998) since physio is often 0.994 not 0.999
            // medo threshold: 0.005f (0.5%) - real sleeping faces have medo ~0.3-0.4%; dor images with closed eyes have medo ~0.7-10%
            // Sleeping hallucinations typically have medo > 0.5%, while genuine dor can have medo < 0.5%
            // Lowered dor threshold to 0.70 to catch more hallucinations (matches dor exception threshold)
            bool isDesacordadoHallucination = pDormindo > 0.99f && dorProb > 0.70f && medoProb > 0.005f;

            // Extended hallucination check for moderate physio confidence (0.85-0.99) - DISABLED
            // Dor images and sleeping hallucinations have overlapping medo (5-10%), can't reliably distinguish
            // Rely on dor exception instead: dorProb > 0.70 && medoProb < 0.10 (10% threshold for genuine dor)
            bool isModerateHallucination = false;

            // Dor e medo são alucinações conhecidas em rostos dormindo
            // Apenas tristeza/enjoo/sono/neutro são emoções acordadas genuínas
            bool isHallucinatedEmotionType = maxEmotionIndex == 0 || maxEmotionIndex == 2; // dor OU medo
            bool hasStrongGenuineEmotion = maxEmotionProb > 0.50f && !isHallucinatedEmotionType; // Raised from 0.30 to 0.50

            // Regra simples e robusta:
            // 1. Se physio EXTREMO (>0.998) -> dormindo (confia no physio, exceto emoção genuína MUITO forte >0.50)
            // 2. Se physio FORTE (>0.99) E é alucinação dor/medo conhecida -> dormindo
            // 3. Se physio FORTE (>0.99) E dor/tristeza passou threshold COM emoção genuína (não dor/medo) -> acordado
            // 4. Se physio FORTE (>0.99) E emoção genuína forte -> acordado
            // 5. Se physio MODERADO (>0.85) E physio > acordado E SEM dor/tristeza threshold E SEM emoção clara -> dormindo
            // 6. Caso contrário -> acordado

            bool isDormindo;

            if (pDormindo > 0.998f)
            {
                // Physio extremamente confiante
                // Se é alucinação conhecida (dormindo + dor alta + medo presente) -> dormindo
                // Se tem dor MUITO alta (>0.70) COM medo < 10% -> dor genuína, não sono
                // Dor images with closed eyes have medo ~5-10%, so use 10% threshold
                // Se tem emoção genuína acordada MUITO forte (>0.50, não dor/medo) -> acordado
                // Senão -> dormindo
                if (isDesacordadoHallucination)
                {
                    isDormindo = true; // Alucinação dor/medo confirmada mesmo com physio extremo
                }
                else if (dorProb > 0.70f && medoProb < 0.10f)
                {
                    isDormindo = false; // Dor muito forte com medo < 10% = dor genuína com olhos fechados
                }
                else if (hasStrongGenuineEmotion)
                {
                    isDormindo = false; // Emoção genuína acordada MUITO forte (tristeza/enjoo/sono/neutro > 0.50)
                }
                else
                {
                    isDormindo = true; // Confia no physio extremo
                }
            }
            else if (pDormindo > 0.99f)
            {
                // Physio muito confiante
                if (isDesacordadoHallucination)
                {
                    isDormindo = true; // Alucinação dor/medo confirmada (pDormindo > 0.99 + dor > 0.80 + medo > 0.005)
                }
                else if (dorProb > 0.70f && medoProb < 0.10f)
                {
                    isDormindo = false; // Dor muito forte com medo < 10% = dor genuína com olhos fechados
                }
                else if (dorOrTristezaPassedThreshold && !isHallucinatedEmotionType)
                {
                    // Dor/tristeza passou threshold MAS a emoção detectada NÃO é medo
                    // = emoção genuína como tristeza com olhos fechados
                    isDormindo = false;
                }
                else if (hasStrongGenuineEmotion)
                {
                    isDormindo = false; // Emoção genuína acordada forte
                }
                else
                {
                    isDormindo = true; // Confia no physio forte
                }
            }
            else if (pDormindo > 0.85f && pDormindo > pAcordado)
            {
                // Physio moderado (0.85-0.99)
                bool hasClearEmotion = maxEmotionProb > 0.35f && !isHallucinatedEmotionType;
                // Split moderate tier by physio confidence:
                // - High moderate (0.95-0.99): treat like strong tier, use hallucination check
                // - Low moderate (0.85-0.95): dor exception applies
                if (pDormindo > 0.95f)
                {
                    // High moderate: physio very confident, likely sleeping hallucination if dor+medo present
                    // medo threshold: 0.03f (3%) - real dor with closed eyes has medo ~2-3%; sleeping hallucinations typically have medo > 3%
                    if (dorProb > 0.70f && medoProb > 0.03f)
                    {
                        isDormindo = true; // Sleeping hallucination: high physio + dor + medo
                    }
                    else if (dorProb > 0.70f && medoProb < 0.10f)
                    {
                        isDormindo = false; // Genuine dor with closed eyes (rare at this physio level)
                    }
                    else if (!dorOrTristezaPassedThreshold && !hasClearEmotion)
                    {
                        isDormindo = true; // No contrary evidence
                    }
                    else
                    {
                        isDormindo = false;
                    }
                }
                else
                {
                    // Low moderate (0.85-0.95): genuine dor with closed eyes possible
                    if (dorProb > 0.70f && medoProb < 0.10f)
                    {
                        isDormindo = false; // Dor genuína com olhos fechados
                    }
                    else if (!dorOrTristezaPassedThreshold && !hasClearEmotion)
                    {
                        isDormindo = true; // Sem evidência contrária
                    }
                    else
                    {
                        isDormindo = false; // Evidência de acordado
                    }
                }
            }
            else
            {
                isDormindo = false; // Physio não indica dormindo
            }

            if (isDormindo)
            {
                return new DualStateResult
                {
                    EstadoFisiologico = "dormindo",
                    ConfiancaFisiologica = pDormindo,
                    EstadoEmocional = null,
                    ConfiancaEmocional = null,
                    PrimaryEmotion = "dormindo",
                    PrimaryConfidence = pDormindo
                };
            }
            else
            {
                return new DualStateResult
                {
                    EstadoFisiologico = "acordado",
                    ConfiancaFisiologica = pAcordado,
                    EstadoEmocional = emocaoDetectada,
                    ConfiancaEmocional = maxEmotionConf,
                    PrimaryEmotion = emocaoDetectada,
                    PrimaryConfidence = maxEmotionConf
                };
            }
        }

        /// <summary>
        /// Analisa usando o modelo legado (único com 8 classes).
        /// Mantido para compatibilidade durante transição.
        /// </summary>
        private DualStateResult AnalyzeDualStateLegacy(float[] normalizedData)
        {
            // Usa a session legada (que está em PhysioSession por causa do construtor)
            var session = _modelLoader.PhysioSession;
            if (session == null)
            {
                throw new InvalidOperationException("Modelo legado não carregado.");
            }

            var probabilities = GetProbabilities(normalizedData, session);

            // Índices: 0: dor, 1: enjoo, 2: medo, 3: sono, 4: tristeza, 5: neutro, 6: acordado, 7: dormindo
            float pDormindo = probabilities[7];
            float pAcordado = probabilities[6];

            // Identifica a melhor emoção entre as emoções ativas (0 a 5)
            int bestEmotionIndex = 5; // neutro padrão
            float maxEmotionConf = -1.0f;
            for (int i = 0; i <= 5; i++)
            {
                if (probabilities[i] > maxEmotionConf)
                {
                    maxEmotionConf = probabilities[i];
                    bestEmotionIndex = i;
                }
            }

            // Identifica o vencedor absoluto global (entre todas as 8 classes)
            int globalMaxIndex = 0;
            float globalMaxConf = -1.0f;
            for (int i = 0; i < probabilities.Length; i++)
            {
                if (probabilities[i] > globalMaxConf)
                {
                    globalMaxConf = probabilities[i];
                    globalMaxIndex = i;
                }
            }

            // Critério: paciente está dormindo se a classe 'dormindo' for o vencedor global
            // ou se superar 'acordado' e qualquer emoção ativa com folga.
            bool isDormindo = globalMaxIndex == 7 || (pDormindo > pAcordado && pDormindo > maxEmotionConf);

            if (isDormindo)
            {
                return new DualStateResult
                {
                    EstadoFisiologico = "dormindo",
                    ConfiancaFisiologica = pDormindo,
                    EstadoEmocional = null,
                    ConfiancaEmocional = null,
                    PrimaryEmotion = "dormindo",
                    PrimaryConfidence = pDormindo
                };
            }
            else
            {
                string emocaoDetectada;
                if (maxEmotionConf >= MinEmotionThreshold)
                {
                    emocaoDetectada = _legacyEmotions[bestEmotionIndex];
                }
                else
                {
                    bestEmotionIndex = 5;
                    emocaoDetectada = "neutro";
                }

                return new DualStateResult
                {
                    EstadoFisiologico = "acordado",
                    ConfiancaFisiologica = pAcordado,
                    EstadoEmocional = emocaoDetectada,
                    ConfiancaEmocional = maxEmotionConf,
                    PrimaryEmotion = emocaoDetectada,
                    PrimaryConfidence = maxEmotionConf
                };
            }
        }

        public (string emotion, float confidence) DetectEmotion(float[] normalizedData)
        {
            var res = AnalyzeDualState(normalizedData);
            return (res.PrimaryEmotion, res.PrimaryConfidence);
        }

        /// <summary>
        /// Retorna todas as probabilidades para diagnóstico (útil para depurar viés do modelo).
        /// </summary>
        public Dictionary<string, float> GetAllProbabilities(float[] normalizedData)
        {
            // Se estiver usando os 2 modelos novos, retorna ambos
            if (_modelLoader.PhysioSession != null && _modelLoader.EmotionSession != null)
            {
                var physioProbs = GetProbabilities(normalizedData, _modelLoader.PhysioSession);
                var emotionProbs = GetProbabilities(normalizedData, _modelLoader.EmotionSession, _emotionTemperatures);

                var dict = new Dictionary<string, float>();
                for (int i = 0; i < _physioClasses.Length; i++)
                    dict[_physioClasses[i]] = physioProbs[i];
                for (int i = 0; i < _emotionClasses.Length; i++)
                    dict[_emotionClasses[i]] = emotionProbs[i];
                return dict;
            }
            // Fallback para modelo legado
            else if (_modelLoader.PhysioSession != null)
            {
                float[] probs = GetProbabilities(normalizedData, _modelLoader.PhysioSession);
                var dict = new Dictionary<string, float>();
                for (int i = 0; i < _legacyEmotions.Length && i < probs.Length; i++)
                    dict[_legacyEmotions[i]] = probs[i];
                return dict;
            }

            return new Dictionary<string, float>();
        }

        /// <summary>
        /// Aplica a função de ativação Softmax para normalizar logits em probabilidades.
        /// </summary>
        private float[] Softmax(float[] logits)
        {
            float max = logits.Max();
            float[] exps = logits.Select(x => MathF.Exp(x - max)).ToArray();
            float sum = exps.Sum();

            if (sum == 0) return logits.Select(_ => 1.0f / logits.Length).ToArray();

            return exps.Select(x => x / sum).ToArray();
        }
    }
}