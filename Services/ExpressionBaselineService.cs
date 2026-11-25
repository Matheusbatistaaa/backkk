using System.Collections.Concurrent;
using PerceptAI.API.Models;

namespace PerceptAI.API.Services
{
    /// <summary>Rosto em repouso aprendido para um leito (referência das expressões).</summary>
    public sealed class ExpressionBaseline
    {
        public float Ear { get; init; }
        public float BrowRaise { get; init; }
        public float BrowSlant { get; init; }
        public float SagUpperLip { get; init; }
        public float SagSeam { get; init; }
        public float SagInner { get; init; }
    }

    /// <summary>
    /// Aprende, sem nenhuma UI, o repouso facial de cada leito: acumula os primeiros
    /// quadros de rosto em repouso e congela a média como referência. Medo e tristeza
    /// passam a ser medidos como DESVIO em relação a esse repouso, o que remove a
    /// dependência de anatomia (sobrancelha naturalmente inclinada, olho naturalmente
    /// grande) e da câmera — causa dos falsos "neutro" na webcam.
    /// </summary>
    public sealed class ExpressionBaselineService
    {
        private const int WarmupSamples = 4;

        private const int MaxWarmupFrames = 4;

        private sealed class Profile
        {
            public int Samples;
            public int Attempts;
            public double Ear, BrowRaise, BrowSlant, SagUpperLip, SagSeam, SagInner;
            public ExpressionBaseline? Frozen;
        }

        private readonly ConcurrentDictionary<string, Profile> _profiles = new();

        public ExpressionBaseline? Get(string? sessionKey)
            => sessionKey is not null && _profiles.TryGetValue(sessionKey, out var profile)
                ? profile.Frozen
                : null;

        /// <summary>
        /// Ainda aprendendo o repouso? Limitado a <see cref="MaxWarmupFrames"/> quadros
        /// para nunca travar a emoção caso nenhum quadro elegível apareça.
        /// </summary>
        public bool IsWarmingUp(string? sessionKey)
        {
            if (sessionKey is null) return false;
            if (!_profiles.TryGetValue(sessionKey, out var profile)) return true;
            lock (profile) return profile.Frozen is null && profile.Attempts <= MaxWarmupFrames;
        }

        /// <summary>
        /// Alimenta o repouso apenas com quadros de face parada: olhos não fechados (olhos
        /// naturalmente estreitos são comuns e NÃO podem bloquear o aprendizado — era o que
        /// deixava webcams de olho pequeno sem repouso e, sem repouso, sem tristeza), sem
        /// bocejo, sem mão na boca, sem dentes à mostra e boca fechada. Congela na quarta
        /// amostra para não aprender expressões que apareçam depois.
        /// </summary>
        public void Observe(string? sessionKey, FacialMetrics metrics)
        {
            if (sessionKey is null) return;

            var profile = _profiles.GetOrAdd(sessionKey, _ => new Profile());

            // "Dentes" com a boca fechada são quase sempre brilho de lábio: isso não pode
            // impedir o aprendizado do repouso. Só pula quando há abertura real ou aperto
            // ocular (padrão de dor de verdade).
            bool noisyTeeth = metrics.TeethVisible
                              && (metrics.MouthAspectRatio >= 0.08f || metrics.EyesTightSqueezed);

            if (metrics.EyesClosed || metrics.EyesAlmostClosed
                || metrics.Yawning || metrics.HandOnMouth || noisyTeeth)
            {
                lock (profile) profile.Attempts++;
                return;
            }
            if (metrics.MouthAspectRatio >= 0.15f || LooksExpressive(metrics))
            {
                lock (profile) profile.Attempts++;
                return;
            }

            lock (profile)
            {
                profile.Attempts++;
                if (profile.Frozen is not null) return;

                profile.Samples++;
                profile.Ear += metrics.EyeAspectRatio;
                profile.BrowRaise += metrics.BrowRaiseRatio;
                profile.BrowSlant += metrics.BrowSlantRatio;
                profile.SagUpperLip += metrics.MouthCornerSagUpperLip;
                profile.SagSeam += metrics.MouthCornerSagRatio;
                profile.SagInner += metrics.MouthCornerSagInner;

                if (profile.Samples >= WarmupSamples)
                {
                    double n = profile.Samples;
                    profile.Frozen = new ExpressionBaseline
                    {
                        Ear = (float)(profile.Ear / n),
                        BrowRaise = (float)(profile.BrowRaise / n),
                        BrowSlant = (float)(profile.BrowSlant / n),
                        SagUpperLip = (float)(profile.SagUpperLip / n),
                        SagSeam = (float)(profile.SagSeam / n),
                        SagInner = (float)(profile.SagInner / n)
                    };
                }
            }
        }

        /// <summary>
        /// Rejeita quadros que já mostram expressão forte (sobrancelha muito inclinada/erguida
        /// ou boca realmente caída) para não congelar um rosto triste como se fosse o repouso.
        /// A barra é alta de propósito: sobrancelha naturalmente inclinada (até ~0,099) e lábio
        /// projetado, que inflam a queda medida no lábio superior/centro, são anatomia e DEVEM
        /// virar repouso. Só a comissura interna separa boca caída de boca "beiçuda" nos
        /// retratos de calibração, então é ela que barra o congelamento.
        /// </summary>
        private static bool LooksExpressive(FacialMetrics m)
            => m.BrowSlantRatio >= 0.100f
               || m.BrowRaiseRatio >= 0.36f
               || m.MouthCornerSagInner >= 0.018f;
    }
}
