using System;
using System.Collections.Generic;
using System.Text;

namespace StillWrong.Meta
{
    public static class MaskCodec
    {
        private const int Radix = 85;
        private const int PrefixLength = 5;
        private const long MaxSafeInteger = 9007199254740991L;
        private const uint Top = 1u << 24;
        private const uint Bottom = 1u << 16;
        private const int Increment = 14;
        private const int CountLimit = 4096;
        private const int ContextCount = 4096;
        private const int Padding = 2;

        private static readonly int[] CharValues = BuildCharValues();

        private static int[] BuildCharValues()
        {
            int[] values = new int[128];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = -1;
            }
            string alphabet = BuildAlphabet();
            for (int i = 0; i < alphabet.Length; i++)
            {
                char c = alphabet[i];
                if (c < 128)
                {
                    values[(int)c] = i;
                }
            }
            return values;
        }

        public static string BuildAlphabet()
        {
            StringBuilder builder = new StringBuilder();
            string excluded = GetExcluded();
            for (int code = 33; code < 127; code++)
            {
                char c = (char)code;
                if (excluded.IndexOf(c) < 0)
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }

        private static string GetExcluded()
        {
            return new string(new char[] { (char)34, (char)92, (char)44, (char)59, (char)60, (char)62, (char)124 });
        }

        private static int Digit(char c)
        {
            int code = (int)c;
            int value = code < 128 ? CharValues[code] : -1;
            if (value < 0 || value >= Radix)
            {
                throw new InvalidMaskPayloadException("Mask payload contains an invalid character.");
            }
            return value;
        }

        public static string Pack(byte[] input)
        {
            int length = input.Length;
            long remaining = length;
            char[] prefix = new char[PrefixLength];
            string alphabet = BuildAlphabet();
            for (int index = PrefixLength - 1; index >= 0; index--)
            {
                prefix[index] = alphabet[(int)(remaining % Radix)];
                remaining = remaining / Radix;
            }
            if (remaining != 0)
            {
                throw new InvalidMaskPayloadException("Mask payload is too large.");
            }
            StringBuilder output = new StringBuilder();
            for (int i = 0; i < prefix.Length; i++)
            {
                output.Append(prefix[i]);
            }
            int offset = 0;
            int full = length - (length % 4);
            while (offset < full)
            {
                uint value = 0;
                unchecked
                {
                    value = ((uint)input[offset] << 24) | ((uint)input[offset + 1] << 16) | ((uint)input[offset + 2] << 8) | (uint)input[offset + 3];
                }
                int[] digits = new int[5];
                unchecked
                {
                    for (int index = 4; index >= 0; index--)
                    {
                        uint d = value % (uint)Radix;
                        digits[index] = (int)d;
                        value = (value - d) / (uint)Radix;
                    }
                }
                for (int i = 0; i < 5; i++)
                {
                    output.Append(alphabet[digits[i]]);
                }
                offset += 4;
            }
            int remainder = length - offset;
            if (remainder > 0)
            {
                uint value = 0;
                unchecked
                {
                    for (int index = 0; index < 4; index++)
                    {
                        uint b = index < remainder ? (uint)input[offset + index] : 0u;
                        value = (value << 8) | b;
                    }
                }
                int[] digits = new int[5];
                unchecked
                {
                    for (int index = 4; index >= 0; index--)
                    {
                        uint d = value % (uint)Radix;
                        digits[index] = (int)d;
                        value = (value - d) / (uint)Radix;
                    }
                }
                for (int index = 0; index < remainder + 1; index++)
                {
                    output.Append(alphabet[digits[index]]);
                }
            }
            return output.ToString();
        }

        public static byte[] Unpack(string payload)
        {
            if (payload == null || payload.Length < PrefixLength)
            {
                throw new InvalidMaskPayloadException("Mask payload is missing its length prefix.");
            }
            long length = 0;
            for (int i = 0; i < PrefixLength; i++)
            {
                length = length * Radix + Digit(payload[i]);
            }
            if (length > MaxSafeInteger)
            {
                throw new InvalidMaskPayloadException("Mask payload length is unsupported.");
            }
            if (length > int.MaxValue)
            {
                throw new InvalidMaskPayloadException("Mask payload length is unsupported.");
            }
            int intLength = (int)length;
            int remainder = intLength % 4;
            int expected = PrefixLength + (intLength / 4) * 5 + (remainder == 0 ? 0 : remainder + 1);
            if (payload.Length != expected)
            {
                throw new InvalidMaskPayloadException("Mask payload length does not match its prefix.");
            }
            byte[] output = new byte[intLength];
            int source = PrefixLength;
            int destination = 0;
            int full = intLength - remainder;
            while (destination < full)
            {
                uint value = 0;
                unchecked
                {
                    for (int index = 0; index < 5; index++)
                    {
                        value = value * (uint)Radix + (uint)Digit(payload[source + index]);
                    }
                }
                source += 5;
                output[destination] = (byte)((value >> 24) & 0xFFu);
                output[destination + 1] = (byte)((value >> 16) & 0xFFu);
                output[destination + 2] = (byte)((value >> 8) & 0xFFu);
                output[destination + 3] = (byte)(value & 0xFFu);
                destination += 4;
            }
            if (remainder > 0)
            {
                uint value = 0;
                unchecked
                {
                    for (int index = 0; index < 5; index++)
                    {
                        uint d = index < remainder + 1 ? (uint)Digit(payload[source + index]) : (uint)(Radix - 1);
                        value = value * (uint)Radix + d;
                    }
                }
                for (int index = 0; index < remainder; index++)
                {
                    output[destination + index] = (byte)((value >> (24 - index * 8)) & 0xFFu);
                }
            }
            string canonical = Pack(output);
            if (!canonical.Equals(payload, StringComparison.Ordinal))
            {
                throw new InvalidMaskPayloadException("Mask payload is not in canonical form.");
            }
            return output;
        }

        private static int ContextAt(byte[] raster, int position, int prior, int earlier)
        {
            return raster[position - 1]
                | (raster[position - 2] << 1)
                | (raster[prior - 2] << 2)
                | (raster[prior - 1] << 3)
                | (raster[prior] << 4)
                | (raster[prior + 1] << 5)
                | (raster[prior + 2] << 6)
                | (raster[earlier - 2] << 7)
                | (raster[earlier - 1] << 8)
                | (raster[earlier] << 9)
                | (raster[earlier + 1] << 10)
                | (raster[earlier + 2] << 11);
        }

        private static void UpdateCounts(int[] counts, int offset, int bit)
        {
            counts[offset + bit] = (counts[offset + bit] + Increment) & 0xFFFF;
            if (counts[offset] + counts[offset + 1] >= CountLimit)
            {
                int zero = counts[offset] >> 1;
                int one = counts[offset + 1] >> 1;
                counts[offset] = zero == 0 ? 1 : zero;
                counts[offset + 1] = one == 0 ? 1 : one;
            }
        }

        internal static string EncodeRaster(byte[] input, int width, int height)
        {
            RangeEncoder encoder = new RangeEncoder();
            int[] counts = new int[ContextCount * 2];
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = 1;
            }
            int paddedWidth = width + 2 * Padding;
            byte[] padded = new byte[paddedWidth * (height + Padding)];
            for (int y = 0; y < height; y++)
            {
                int row = (y + Padding) * paddedWidth + Padding;
                for (int x = 0; x < width; x++)
                {
                    int position = row + x;
                    int context = ContextAt(padded, position, position - paddedWidth, position - 2 * paddedWidth);
                    int offset = context << 1;
                    int zero = counts[offset];
                    int one = counts[offset + 1];
                    int bit = input[y * width + x];
                    if (bit != 0 && bit != 1)
                    {
                        throw new InvalidMaskPayloadException("Decoded mask is not binary.");
                    }
                    if (bit == 0)
                    {
                        encoder.Encode(0u, (uint)zero, (uint)(zero + one));
                    }
                    else
                    {
                        encoder.Encode((uint)zero, (uint)one, (uint)(zero + one));
                    }
                    padded[position] = (byte)bit;
                    UpdateCounts(counts, offset, bit);
                }
            }
            return "!" + Pack(encoder.Finish());
        }

        public static byte[] DecodeOneBit(string payload, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new InvalidMaskPayloadException("Mask dimensions must be positive.");
            }
            if (payload == null || payload.Length == 0)
            {
                throw new InvalidMaskPayloadException("Mask payload is missing.");
            }
            string packed = payload;
            byte[] unpacked;
            try
            {
                unpacked = Unpack(packed);
            }
            catch (UnsupportedMaskEncodingException)
            {
                throw;
            }
            catch (InvalidMaskPayloadException)
            {
                throw;
            }
            catch (Exception inner)
            {
                throw new InvalidMaskPayloadException("Mask payload could not be decoded.", inner);
            }
            byte[] raster;
            try
            {
                raster = DecodeRaster(unpacked, width, height);
            }
            catch (InvalidMaskPayloadException)
            {
                throw;
            }
            catch (Exception inner)
            {
                throw new InvalidMaskPayloadException("Mask payload could not be decoded.", inner);
            }
            string canonical = EncodeRaster(raster, width, height);
            if (!canonical.Substring(1).Equals(packed, StringComparison.Ordinal))
            {
                throw new InvalidMaskPayloadException("Mask payload is not canonical or has invalid finalization.");
            }
            return raster;
        }

        public static byte[] DecodeMask(string payload, int width, int height, SamMaskEncoding encoding)
        {
            if (encoding == SamMaskEncoding.Lossless)
            {
                throw new UnsupportedMaskEncodingException("Lossless mask encoding is not supported in this build.");
            }
            return DecodeOneBit(payload, width, height);
        }

        private static byte[] DecodeRaster(byte[] input, int width, int height)
        {
            RangeDecoder decoder = new RangeDecoder(input);
            int[] counts = new int[ContextCount * 2];
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = 1;
            }
            int paddedWidth = width + 2 * Padding;
            byte[] padded = new byte[paddedWidth * (height + Padding)];
            byte[] output = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = (y + Padding) * paddedWidth + Padding;
                for (int x = 0; x < width; x++)
                {
                    int position = row + x;
                    int context = ContextAt(padded, position, position - paddedWidth, position - 2 * paddedWidth);
                    int offset = context << 1;
                    int zero = counts[offset];
                    int one = counts[offset + 1];
                    uint total = (uint)(zero + one);
                    uint value = decoder.Frequency(total);
                    int bit = value < (uint)zero ? 0 : 1;
                    if (bit == 0)
                    {
                        decoder.Decode(0u, (uint)zero);
                    }
                    else
                    {
                        decoder.Decode((uint)zero, (uint)one);
                    }
                    padded[position] = (byte)bit;
                    output[y * width + x] = (byte)bit;
                    UpdateCounts(counts, offset, bit);
                }
            }
            return output;
        }

        private sealed class RangeEncoder
        {
            private uint low;
            private uint range;
            private List<byte> output;

            public RangeEncoder()
            {
                low = 0u;
                range = 0xFFFFFFFFu;
                output = new List<byte>();
            }

            public void Encode(uint cumulative, uint frequency, uint total)
            {
                uint scaled;
                unchecked
                {
                    scaled = range / total;
                    low = low + scaled * cumulative;
                    range = scaled * frequency;
                }
                while (true)
                {
                    uint sum;
                    unchecked
                    {
                        sum = low + range;
                    }
                    if ((low ^ sum) < Top)
                    {
                    }
                    else if (range < Bottom)
                    {
                        unchecked
                        {
                            range = (0u - low) & (Bottom - 1u);
                        }
                    }
                    else
                    {
                        break;
                    }
                    output.Add((byte)((low >> 24) & 0xFFu));
                    unchecked
                    {
                        low = low << 8;
                        range = range << 8;
                    }
                }
            }

            public byte[] Finish()
            {
                for (int i = 0; i < 4; i++)
                {
                    output.Add((byte)((low >> 24) & 0xFFu));
                    unchecked
                    {
                        low = low << 8;
                    }
                }
                return output.ToArray();
            }
        }

        private sealed class RangeDecoder
        {
            private readonly byte[] input;
            private int position;
            private uint low;
            private uint range;
            private uint code;
            private uint scaled;

            public RangeDecoder(byte[] data)
            {
                if (data == null || data.Length < 4)
                {
                    throw new InvalidMaskPayloadException("Mask payload is missing its finalization.");
                }
                input = data;
                position = 0;
                low = 0u;
                range = 0xFFFFFFFFu;
                code = 0u;
                scaled = 0u;
                for (int i = 0; i < 4; i++)
                {
                    unchecked
                    {
                        code = (code << 8) | Read();
                    }
                }
            }

            public uint Frequency(uint total)
            {
                unchecked
                {
                    scaled = range / total;
                }
                if (scaled == 0u)
                {
                    throw new InvalidMaskPayloadException("Mask payload has invalid coding state.");
                }
                uint value;
                unchecked
                {
                    value = (code - low) / scaled;
                }
                return value >= total ? total - 1u : value;
            }

            public void Decode(uint cumulative, uint frequency)
            {
                unchecked
                {
                    low = low + scaled * cumulative;
                    range = scaled * frequency;
                }
                while (true)
                {
                    uint sum;
                    unchecked
                    {
                        sum = low + range;
                    }
                    if ((low ^ sum) < Top)
                    {
                    }
                    else if (range < Bottom)
                    {
                        unchecked
                        {
                            range = (0u - low) & (Bottom - 1u);
                        }
                    }
                    else
                    {
                        break;
                    }
                    unchecked
                    {
                        code = (code << 8) | Read();
                        low = low << 8;
                        range = range << 8;
                    }
                }
            }

            private uint Read()
            {
                if (position >= input.Length)
                {
                    throw new InvalidMaskPayloadException("Mask payload ended before decoding completed.");
                }
                uint value = input[position];
                position++;
                return value;
            }
        }
    }
}
