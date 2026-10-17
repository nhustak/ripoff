using System;
using Microsoft.Xna.Framework.Audio;
using RipOff.Core.Audio;

namespace RipOff.Game
{
    /// <summary>
    /// Streams the synthesized sound board (RipOff.Core.Audio.SoundBoard, NOTES §21) through a MonoGame
    /// DynamicSoundEffectInstance: 48 kHz mono 16-bit PCM, one buffer per simulation pass. No content pipeline,
    /// no files: every sample is computed at runtime. Without an audio device the board still runs (so its state
    /// stays in step) and nothing is played; any audio exception disables output instead of crashing the game.
    /// </summary>
    public sealed class SoundOutput : IDisposable
    {
        /// <summary>Buffers queued before playback starts (~53 ms of latency).</summary>
        public const int Prime = 2;
        /// <summary>Above this many queued buffers a pass is dropped (the game ran fast; keeps latency bounded).</summary>
        public const int MaxPending = 4;

        private readonly SoundBoard _board = new SoundBoard();
        private readonly float[] _f = new float[SoundBoard.MaxPassSamples];
        private readonly byte[] _b = new byte[SoundBoard.MaxPassSamples * 2];
        private readonly byte[] _silence = new byte[SoundBoard.MaxPassSamples * 2];
        private DynamicSoundEffectInstance? _inst;

        public string Status { get; private set; } = "audio off";
        public bool Available => _inst != null;
        public SoundBoard Board => _board;

        public SoundOutput(bool enable = true)
        {
            if (!enable) { Status = "audio disabled"; return; }
            try
            {
                _inst = new DynamicSoundEffectInstance(SoundBoard.SampleRate, AudioChannels.Mono);
                for (int i = 0; i < Prime; i++) _inst.SubmitBuffer(_silence, 0, 1263 * 2);
                _inst.Play();
                Status = "audio on";
            }
            catch (NoAudioHardwareException e) { Disable("no audio hardware: " + e.Message); }
            catch (Exception e) { Disable("audio unavailable: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Renders one pass of <paramref name="s"/> and queues it.</summary>
        public void Update(in RipOff.Core.SoundState s)
        {
            int n = _board.RenderPass(s, _f);
            if (_inst == null) return;
            try
            {
                int pending = _inst.PendingBufferCount;
                if (pending > MaxPending) return;
                if (pending == 0) _inst.SubmitBuffer(_silence, 0, 2 * n); // underrun: rebuild the cushion with silence
                _inst.SubmitBuffer(_b, 0, ToPcm16(_f, n, _b));
            }
            catch (NoAudioHardwareException e) { Disable("no audio hardware: " + e.Message); }
            catch (Exception e) { Disable("audio error: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>Float (−1..1) → little-endian signed 16-bit. Returns the byte count.</summary>
        public static int ToPcm16(float[] src, int n, byte[] dst)
        {
            for (int i = 0; i < n; i++)
            {
                float v = src[i];
                if (float.IsNaN(v)) v = 0;
                int q = (int)MathF.Round(Math.Clamp(v, -1f, 1f) * 32767f);
                dst[2 * i] = (byte)q; dst[2 * i + 1] = (byte)(q >> 8);
            }
            return 2 * n;
        }

        private void Disable(string why)
        {
            Status = why;
            Console.Error.WriteLine("RipOff: " + why + " (continuing without sound)");
            try { _inst?.Dispose(); } catch (Exception) { /* already broken */ }
            _inst = null;
        }

        public void Dispose() { try { _inst?.Stop(); _inst?.Dispose(); } catch (Exception) { } _inst = null; }
    }
}
