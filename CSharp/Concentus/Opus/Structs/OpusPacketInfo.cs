﻿/* Copyright (c) 2007-2008 CSIRO
   Copyright (c) 2007-2011 Xiph.Org Foundation
   Originally written by Jean-Marc Valin, Gregory Maxwell, Koen Vos,
   Timothy B. Terriberry, and the Opus open-source contributors
   Ported to C# by Logan Stromberg

   Redistribution and use in source and binary forms, with or without
   modification, are permitted provided that the following conditions
   are met:

   - Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.

   - Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.

   - Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
   ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
   LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
   A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER
   OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
   EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
   PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
   PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
   LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
   NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
   SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Concentus.Structs
{
    public class OpusPacketInfo
    {
        /// <summary>
        /// The Table of Contents byte for this packet. Contains info about modes, frame length, etc.
        /// </summary>
        public readonly sbyte TOCByte;

        /// <summary>
        /// The list of subframes in this packet
        /// </summary>
        public readonly IList<byte[]> Frames;

        /// <summary>
        /// The index of the start of the payload within the packet
        /// </summary>
        public readonly int PayloadOffset;

        private OpusPacketInfo(sbyte toc, IList<byte[]> frames, int payloadOffset)
        {
            TOCByte = toc;
            Frames = frames;
            PayloadOffset = payloadOffset;
        }

        /// <summary>
        /// Parse an opus packet into a packetinfo object containing one or more frames.
        /// Opus_decode will perform this operation internally so most applications do
        /// not need to use this function.
        /// </summary>
        /// <param name="packet">The packet data to be parsed</param>
        /// <param name="packet_offset">The index of the beginning of the packet in the data array (usually 0)</param>
        /// <param name="len">The packet's length</param>
        /// <returns>A parsed packet info struct</returns>
        public static OpusPacketInfo ParseOpusPacket(byte[] packet, int packet_offset, int len)
        {
            // Find the number of frames first
            int numFrames = GetNumFrames(packet, packet_offset, len);

            int payload_offset;
            sbyte out_toc;
            byte[][] frames = new byte[numFrames][];
            short[] size = new short[numFrames];
            int packetOffset;
            int error = opus_packet_parse_impl(packet, packet_offset, len, 0, out out_toc, frames, 0, size, 0, out payload_offset, out packetOffset);
            if (error < 0)
            {
                throw new OpusException("An error occurred while parsing the packet", error);
            }

            // Since packet_parse_impl has created deep copies of each frame, we can return them safely from this function without
            // worrying about variable scoping or side effects
            IList<byte[]> copiedFrames = new List<byte[]>(frames);

            return new OpusPacketInfo(out_toc, copiedFrames, payload_offset);
        }
        
        public static int GetNumSamplesPerFrame(byte[] packet, int packet_offset, int Fs)
        {
            return GetNumSamplesPerFrame(EntropyCoder.Convert(packet), packet_offset, Fs);
        }

        /// <summary>
        /// Gets the number of samples per frame from an Opus packet.
        /// </summary>
        /// <param name="data">Opus packet. This must contain at least one byte of data</param>
        /// <param name="Fs">Sampling rate in Hz. This must be a multiple of 400, or inaccurate results will be returned.</param>
        /// <returns>Number of samples per frame</returns>
        public static int GetNumSamplesPerFrame(sbyte[] packet, int packet_offset, int Fs)
        {
            int audiosize;
            if ((packet[packet_offset] & 0x80) != 0)
            {
                audiosize = ((packet[packet_offset] >> 3) & 0x3);
                audiosize = (Fs << audiosize) / 400;
            }
            else if ((packet[packet_offset] & 0x60) == 0x60)
            {
                audiosize = ((packet[packet_offset] & 0x08) != 0) ? Fs / 50 : Fs / 100;
            }
            else
            {
                audiosize = ((packet[packet_offset] >> 3) & 0x3);
                if (audiosize == 3)
                    audiosize = Fs * 60 / 1000;
                else
                    audiosize = (Fs << audiosize) / 100;
            }
            return audiosize;
        }
        
        public static OpusBandwidth GetBandwidth(byte[] packet, int packet_offset)
        {
            return GetBandwidth(EntropyCoder.Convert(packet), packet_offset);
        }

        /// <summary>
        /// Gets the encoded bandwidth of an Opus packet. Note that you are not forced to decode at this bandwidth
        /// </summary>
        /// <param name="data">An Opus packet (must be at least 1 byte)</param>
        /// <returns>An OpusBandwidth value</returns>
        public static OpusBandwidth GetBandwidth(sbyte[] packet, int packet_offset)
        {
            OpusBandwidth bandwidth;
            if ((packet[packet_offset] & 0x80) != 0)
            {
                bandwidth = OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND + ((packet[packet_offset] >> 5) & 0x3);
                if (bandwidth == OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND)
                    bandwidth = OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND;
            }
            else if ((packet[packet_offset] & 0x60) == 0x60)
            {
                bandwidth = ((packet[packet_offset] & 0x10) != 0) ? OpusBandwidth.OPUS_BANDWIDTH_FULLBAND :
                                             OpusBandwidth.OPUS_BANDWIDTH_SUPERWIDEBAND;
            }
            else {
                bandwidth = OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND + ((packet[packet_offset] >> 5) & 0x3);
            }
            return bandwidth;
        }
        
        public static int GetNumEncodedChannels(byte[] packet, int packet_offset)
        {
            return GetNumEncodedChannels(EntropyCoder.Convert(packet), packet_offset);
        }

        /// <summary>
        /// Gets the number of encoded channels of an Opus packet. Note that you are not forced to decode with this channel count.
        /// </summary>
        /// <param name="data">An opus packet (must be at least 1 byte)</param>
        /// <returns>The number of channels</returns>
        public static int GetNumEncodedChannels(sbyte[] packet, int packet_offset)
        {
            return ((packet[packet_offset] & 0x4) != 0) ? 2 : 1;
        }
        
        public static int GetNumFrames(byte[] packet, int packet_offset, int len)
        {
            return GetNumFrames(EntropyCoder.Convert(packet), packet_offset, len);
        }

        /// <summary>
        /// Gets the number of frames in an Opus packet.
        /// </summary>
        /// <param name="packet">An Opus packet</param>
        /// <param name="len">The packet's length (must be at least 1)</param>
        /// <returns>The number of frames in the packet</returns>
        public static int GetNumFrames(sbyte[] packet, int packet_offset, int len)
        {
            int count;
            if (len < 1)
                return OpusError.OPUS_BAD_ARG;
            count = packet[packet_offset] & 0x3;
            if (count == 0)
                return 1;
            else if (count != 3)
                return 2;
            else if (len < 2)
                return OpusError.OPUS_INVALID_PACKET;
            else
                return packet[packet_offset + 1] & 0x3F;
        }
        
        public static int GetNumSamples(byte[] packet, int packet_offset, int len,
              int Fs)
        {
            return GetNumSamples(EntropyCoder.Convert(packet), packet_offset, len, Fs);
        }

        /// <summary>
        /// Gets the number of samples of an Opus packet.
        /// </summary>
        /// <param name="packet">An Opus packet</param>
        /// <param name="len">The packet's length</param>
        /// <param name="Fs">The decoder's sampling rate in Hz. This must be a multiple of 400</param>
        /// <returns>The size of the PCM samples that this packet will be decoded to at the specified sample rate</returns>
        public static int GetNumSamples(sbyte[] packet, int packet_offset, int len,
              int Fs)
        {
            int samples;
            int count = GetNumFrames(packet, packet_offset, len);

            if (count < 0)
                return count;

            samples = count * GetNumSamplesPerFrame(packet, packet_offset, Fs);
            /* Can't have more than 120 ms */
            if (samples * 25 > Fs * 3)
                return OpusError.OPUS_INVALID_PACKET;
            else
                return samples;
        }

        /// <summary>
        /// Gets the number of samples of an Opus packet.
        /// </summary>
        /// <param name="dec">Your current decoder state</param>
        /// <param name="packet">An Opus packet</param>
        /// <param name="len">The packet's length</param>
        /// <returns>The size of the PCM samples that this packet will be decoded to by the specified decoder</returns>
        public static int GetNumSamples(OpusDecoder dec,
              byte[] packet, int packet_offset, int len)
        {
            return GetNumSamples(packet, packet_offset, len, dec.Fs);
        }
        
        public static OpusMode GetEncoderMode(byte[] packet, int packet_offset)
        {
            return GetEncoderMode(EntropyCoder.Convert(packet), packet_offset);
        }

        /// <summary>
        /// Gets the mode that was used to encode this packet.
        /// Normally there is nothing you can really do with this, other than debugging.
        /// </summary>
        /// <param name="data">An Opus packet</param>
        /// <returns>The OpusMode used by the encoder</returns>
        public static OpusMode GetEncoderMode(sbyte[] packet, int packet_offset)
        {
            OpusMode mode;
            if ((packet[packet_offset] & 0x80) != 0)
            {
                mode = OpusMode.MODE_CELT_ONLY;
            }
            else if ((packet[packet_offset] & 0x60) == 0x60)
            {
                mode = OpusMode.MODE_HYBRID;
            }
            else {
                mode = OpusMode.MODE_SILK_ONLY;
            }
            return mode;
        }

        internal static int encode_size(int size, byte[] data, int data_ptr)
        {
            sbyte[] dummy = new sbyte[2];
            int t = encode_size(size, dummy, 0);
            data[data_ptr] = EntropyCoder.Convert(dummy[0]);
            if (t == 2)
                data[data_ptr + 1] = EntropyCoder.Convert(dummy[1]);
            return t;
        }

        internal static int encode_size(int size, sbyte[] data, int data_ptr)
        {
            if (size < 252)
            {
                data[data_ptr] = (sbyte)(size & 0xFF);
                return 1;
            }
            else {
                int dp1 = 252 + (size & 0x3);
                data[data_ptr] = (sbyte)(dp1 & 0xFF);
                data[data_ptr + 1] = (sbyte)((size - dp1) >> 2);
                return 2;
            }
        }

        internal static int parse_size(byte[] data, int data_ptr, int len, BoxedValue<short> size)
        {
            return parse_size(EntropyCoder.Convert(data), data_ptr, len, size);
        }

        internal static int parse_size(sbyte[] data, int data_ptr, int len, BoxedValue<short> size)
        {
            if (len < 1)
            {
                size.Val = -1;
                return -1;
            }
            else if (Inlines.SignedByteToUnsignedInt(data[data_ptr]) < 252)
            {
                size.Val = (short)Inlines.SignedByteToUnsignedInt(data[data_ptr]);
                return 1;
            }
            else if (len < 2)
            {
                size.Val = -1;
                return -1;
            }
            else {
                size.Val = (short)(4 * Inlines.SignedByteToUnsignedInt(data[data_ptr + 1]) + Inlines.SignedByteToUnsignedInt(data[data_ptr]));
                return 2;
            }
        }

        internal static int opus_packet_parse_impl(byte[] data, int data_ptr, int len,
              int self_delimited, out sbyte out_toc,
              byte[][] frames, int frames_ptr, short[] sizes, int sizes_ptr,
              out int payload_offset, out int packet_offset)
        {
            int i, bytes;
            int count;
            int cbr;
            sbyte toc;
            int ch;
            int framesize;
            int last_size;
            int pad = 0;
            int data0 = data_ptr;
            out_toc = 0;
            payload_offset = 0;
            packet_offset = 0;

            if (sizes == null || len < 0)
                return OpusError.OPUS_BAD_ARG;
            if (len == 0)
                return OpusError.OPUS_INVALID_PACKET;

            framesize = GetNumSamplesPerFrame(data, data_ptr, 48000);

            cbr = 0;
            toc = EntropyCoder.Convert(data[data_ptr++]);
            len--;
            last_size = len;
            switch (toc & 0x3)
            {
                /* One frame */
                case 0:
                    count = 1;
                    break;
                /* Two CBR frames */
                case 1:
                    count = 2;
                    cbr = 1;
                    if (self_delimited == 0)
                    {
                        if ((len & 0x1) != 0)
                            return OpusError.OPUS_INVALID_PACKET;
                        last_size = len / 2;
                        /* If last_size doesn't fit in size[0], we'll catch it later */
                        sizes[sizes_ptr] = (short)last_size;
                    }
                    break;
                /* Two VBR frames */
                case 2:
                    count = 2;
                    BoxedValue<short> boxed_size = new BoxedValue<short>(sizes[sizes_ptr]);
                    bytes = parse_size(data, data_ptr, len, boxed_size);
                    sizes[sizes_ptr] = boxed_size.Val;
                    len -= bytes;
                    if (sizes[sizes_ptr] < 0 || sizes[sizes_ptr] > len)
                        return OpusError.OPUS_INVALID_PACKET;
                    data_ptr += bytes;
                    last_size = len - sizes[sizes_ptr];
                    break;
                /* Multiple CBR/VBR frames (from 0 to 120 ms) */
                default: /*case 3:*/
                    if (len < 1)
                        return OpusError.OPUS_INVALID_PACKET;
                    /* Number of frames encoded in bits 0 to 5 */
                    ch = Inlines.SignedByteToUnsignedInt(data[data_ptr++]);
                    count = ch & 0x3F;
                    if (count <= 0 || framesize * count > 5760)
                        return OpusError.OPUS_INVALID_PACKET;
                    len--;
                    /* Padding flag is bit 6 */
                    if ((ch & 0x40) != 0)
                    {
                        int p;
                        do
                        {
                            int tmp;
                            if (len <= 0)
                                return OpusError.OPUS_INVALID_PACKET;
                            p = Inlines.SignedByteToUnsignedInt(data[data_ptr++]);
                            len--;
                            tmp = p == 255 ? 254 : p;
                            len -= tmp;
                            pad += tmp;
                        } while (p == 255);
                    }
                    if (len < 0)
                        return OpusError.OPUS_INVALID_PACKET;
                    /* VBR flag is bit 7 */
                    cbr = (ch & 0x80) != 0 ? 0 : 1;
                    if (cbr == 0)
                    {
                        /* VBR case */
                        last_size = len;
                        for (i = 0; i < count - 1; i++)
                        {
                            boxed_size = new BoxedValue<short>(sizes[sizes_ptr + i]);
                            bytes = parse_size(data, data_ptr, len, boxed_size);
                            sizes[sizes_ptr + i] = boxed_size.Val;
                            len -= bytes;
                            if (sizes[sizes_ptr + i] < 0 || sizes[sizes_ptr + i] > len)
                                return OpusError.OPUS_INVALID_PACKET;
                            data_ptr += bytes;
                            last_size -= bytes + sizes[sizes_ptr + i];
                        }
                        if (last_size < 0)
                            return OpusError.OPUS_INVALID_PACKET;
                    }
                    else if (self_delimited == 0)
                    {
                        /* CBR case */
                        last_size = len / count;
                        if (last_size * count != len)
                            return OpusError.OPUS_INVALID_PACKET;
                        for (i = 0; i < count - 1; i++)
                            sizes[sizes_ptr + i] = (short)last_size;
                    }
                    break;
            }

            /* Self-delimited framing has an extra size for the last frame. */
            if (self_delimited != 0)
            {
                BoxedValue<short> boxed_size = new BoxedValue<short>(sizes[sizes_ptr + count - 1]);
                bytes = parse_size(data, data_ptr, len, boxed_size);
                sizes[sizes_ptr + count - 1] = boxed_size.Val;
                len -= bytes;
                if (sizes[sizes_ptr + count - 1] < 0 || sizes[sizes_ptr + count - 1] > len)
                    return OpusError.OPUS_INVALID_PACKET;
                data_ptr += bytes;
                /* For CBR packets, apply the size to all the frames. */
                if (cbr != 0)
                {
                    if (sizes[sizes_ptr + count - 1] * count > len)
                        return OpusError.OPUS_INVALID_PACKET;
                    for (i = 0; i < count - 1; i++)
                        sizes[sizes_ptr + i] = sizes[sizes_ptr + count - 1];
                }
                else if (bytes + sizes[sizes_ptr + count - 1] > last_size)
                    return OpusError.OPUS_INVALID_PACKET;
            }
            else
            {
                /* Because it's not encoded explicitly, it's possible the size of the
                   last packet (or all the packets, for the CBR case) is larger than
                   1275. Reject them here.*/
                if (last_size > 1275)
                    return OpusError.OPUS_INVALID_PACKET;
                sizes[sizes_ptr + count - 1] = (short)last_size;
            }

            payload_offset = (int)(data_ptr - data0);

            for (i = 0; i < count; i++)
            {
                if (frames != null)
                {
                    // The old code returned pointers to the single data array, but that can cause unwanted side effects.
                    // So I have replaced it with this code that creates a new copy of each frame. Slower, but more robust
                    frames[frames_ptr + i] = new byte[data.Length - data_ptr];
                    Array.Copy(data, data_ptr, frames[frames_ptr + i], 0, data.Length - data_ptr);
                }
                data_ptr += sizes[sizes_ptr + i];
            }

            packet_offset = pad + (int)(data_ptr - data0);

            out_toc = toc;

            return count;
        }
    }
}
