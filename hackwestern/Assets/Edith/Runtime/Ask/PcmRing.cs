using UnityEngine;

namespace Edith.Ask
{
    /// Answer speech waiting to play, as 16-bit samples. The socket's receive thread writes it and the audio thread
    /// reads it, so every access takes the lock.
    public sealed class PcmRing
    {
        const float Scale = 1f / 32768f;

        readonly short[] samples;
        readonly object gate = new object();
        int head;
        int count;

        public PcmRing(int capacity) => samples = new short[capacity];

        public int Count
        {
            get
            {
                lock (gate) return count;
            }
        }

        /// Appends 16-bit little-endian PCM. When it does not all fit, keeps what fits and returns false.
        public bool Write(byte[] pcm)
        {
            var incoming = pcm.Length / 2;
            lock (gate)
            {
                var room = samples.Length - count;
                var taken = Mathf.Min(incoming, room);
                var tail = (head + count) % samples.Length;
                for (var i = 0; i < taken; i++)
                {
                    samples[tail] = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8));
                    if (++tail == samples.Length) tail = 0;
                }
                count += taken;
                return taken == incoming;
            }
        }

        /// Fills `into` with what is waiting, then silence. Returns how many samples it took.
        public int Read(float[] into)
        {
            lock (gate)
            {
                var taken = Mathf.Min(into.Length, count);
                for (var i = 0; i < taken; i++)
                {
                    into[i] = samples[head] * Scale;
                    if (++head == samples.Length) head = 0;
                }
                for (var i = taken; i < into.Length; i++) into[i] = 0f;
                count -= taken;
                return taken;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                head = 0;
                count = 0;
            }
        }
    }

    public static class Pcm16
    {
        /// Writes `count` samples as 16-bit little-endian PCM into `into`, clipping past full scale. Returns the bytes
        /// written.
        public static int Write(float[] samples, int count, byte[] into)
        {
            for (var i = 0; i < count; i++)
            {
                var value = (short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                into[2 * i] = (byte)value;
                into[2 * i + 1] = (byte)(value >> 8);
            }
            return count * 2;
        }
    }
}
