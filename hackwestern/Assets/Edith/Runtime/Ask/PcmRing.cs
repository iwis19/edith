using UnityEngine;

namespace Edith.Ask
{
    /// Answer speech waiting to play, as 16-bit samples. The socket's receive thread writes it and the audio thread
    /// reads it, so every access takes the lock. Reading waits until `prebuffer` samples are in, at the start and
    /// again after running dry, unless Finish says no more are coming.
    public sealed class PcmRing
    {
        const float Scale = 1f / 32768f;

        readonly short[] samples;
        readonly int prebuffer;
        readonly object gate = new object();
        int head;
        int count;
        bool flowing;
        bool finished;
        long played;
        int underruns;
        int readLength;

        public PcmRing(int capacity, int prebuffer = 0)
        {
            samples = new short[capacity];
            this.prebuffer = prebuffer;
        }

        public int Count
        {
            get
            {
                lock (gate) return count;
            }
        }

        /// Samples read out since the last Clear.
        public long Played
        {
            get
            {
                lock (gate) return played;
            }
        }

        /// Times reading ran dry before Finish since the last Clear.
        public int Underruns
        {
            get
            {
                lock (gate) return underruns;
            }
        }

        /// How many samples the last Read asked for.
        public int ReadLength
        {
            get
            {
                lock (gate) return readLength;
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

        /// No more speech is coming: what is waiting plays out to the end without waiting for the prebuffer.
        public void Finish()
        {
            lock (gate) finished = true;
        }

        /// Fills `into` with what is waiting, then silence. Returns how many samples it took.
        public int Read(float[] into)
        {
            lock (gate)
            {
                readLength = into.Length;
                if (!flowing && (count >= prebuffer || finished)) flowing = true;
                var taken = flowing ? Mathf.Min(into.Length, count) : 0;
                for (var i = 0; i < taken; i++)
                {
                    into[i] = samples[head] * Scale;
                    if (++head == samples.Length) head = 0;
                }
                for (var i = taken; i < into.Length; i++) into[i] = 0f;
                count -= taken;
                played += taken;
                if (flowing && taken < into.Length && !finished)
                {
                    flowing = false;
                    if (played > 0) underruns++;
                }
                return taken;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                head = 0;
                count = 0;
                flowing = false;
                finished = false;
                played = 0;
                underruns = 0;
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
