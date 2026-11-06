using System;

namespace opentuner
{
    // Chooses which video and audio PID reach the media player. It sits between the TS thread and the player's buffer (not in front of the
    // recorder, the UDP streamer or the TS analysis, they keep the whole stream).
    //
    // Automatic (default): every byte goes through unchanged. Manual: whole TS packets only, and of those only the system PIDs (PAT, SDT,
    // NIT ... below 0x20), the PMTs the PAT announces, the PCR PIDs the PMTs name, the null packets and the chosen video and audio PID. The PMT itself is not
    // rewritten, so it still lists the other streams; they just have no packets any more.
    //
    // The PAT and PMTs are learnt while the filter is in the automatic mode too (Learn = true), so that the PMT PIDs are known the moment the
    // manual mode is switched on.
    public class TsPidFilter
    {
        private const int PacketSize = 188;
        private const int PidCount = 8192;

        private volatile bool _manual = false;
        private volatile int _video_pid = -1;
        private volatile int _audio_pid = -1;
        private volatile bool _learn = false;

        private bool _was_filtering = false;   // the mode the last bytes were handled in, a change drops the packet in progress

        private readonly byte[] _packet = new byte[PacketSize];
        private int _packet_fill = 0;
        private bool[] _dynamic_pids = new bool[PidCount];   // PMT PIDs and PCR PIDs
        private readonly bool[] _pcr_pids = new bool[PidCount];
        private readonly bool[] _pcr_stream = new bool[PidCount];   // PCR PIDs that are streams of the PMT (the video, mostly)
        private readonly byte[] _pcr_packet = new byte[PacketSize];
        private readonly byte[] _pmt_packet = new byte[PacketSize];
        private bool[] _pmt_pids = new bool[PidCount];               // the PMT PIDs of the PAT

        // true: PAT and PMTs are read to know the PMT and PCR PIDs, also while nothing is filtered (the MiniTiouner source sets it)
        public bool Learn
        {
            get { return _learn; }
            set { _learn = value; }
        }

        public bool Manual
        {
            get { return _manual; }
        }

        // all streams unchanged
        public void SetAutomatic()
        {
            _manual = false;
        }

        // only these PIDs (0 / -1 = none of that kind) besides the system PIDs
        public void SetManual(int video_pid, int audio_pid)
        {
            _video_pid = video_pid;
            _audio_pid = audio_pid;
            _manual = true;
        }

        // forget what was learnt and drop the packet in progress (new tune, the stream starts again)
        public void Reset()
        {
            _packet_fill = 0;
            _dynamic_pids = new bool[PidCount];
            _pmt_pids = new bool[PidCount];
            Array.Clear(_pcr_pids, 0, PidCount);
            Array.Clear(_pcr_stream, 0, PidCount);
        }

        // Hands the bytes of one read to the queue of the player.
        public void Push(byte[] data, int count, CircularBuffer queue)
        {
            bool manual = _manual;

            if (!manual && !_learn)
            {
                // nothing to filter, nothing to learn: as before the filter existed
                _was_filtering = false;
                for (int c = 0; c < count; c++)
                    queue.Enqueue(data[c]);
                return;
            }

            // a change of the mode in the middle of a packet: wait for the next packet start
            if (manual != _was_filtering)
            {
                _packet_fill = 0;
                _was_filtering = manual;
            }

            for (int c = 0; c < count; c++)
            {
                byte b = data[c];

                if (!manual)
                    queue.Enqueue(b);

                if (_packet_fill == 0 && b != 0x47)
                    continue;   // not at a packet start

                _packet[_packet_fill++] = b;
                if (_packet_fill < PacketSize)
                    continue;

                _packet_fill = 0;
                Learnt(_packet);

                if (manual)
                {
                    if (_pmt_pids[PidOf(_packet)] && (_packet[1] & 0x40) != 0 && RewritePmt(_packet, _pmt_packet))
                    {
                        // the PMT names only the chosen streams: a player waits for every stream a PMT names (it has to analyse each of them
                        // before it shows anything), so one that is no longer sent must not be named
                        for (int n = 0; n < PacketSize; n++)
                            queue.Enqueue(_pmt_packet[n]);
                    }
                    else if (Accepted(_packet))
                    {
                        for (int n = 0; n < PacketSize; n++)
                            queue.Enqueue(_packet[n]);
                    }
                    else if (_pcr_stream[PidOf(_packet)] && PcrOnly(_packet, _pcr_packet))
                    {
                        // a stream that carries the PCR but was not chosen (usually the video): only its clock reference goes on, so that the
                        // chosen stream (the audio) keeps its time base
                        for (int n = 0; n < PacketSize; n++)
                            queue.Enqueue(_pcr_packet[n]);
                    }
                }
            }
        }

        // Makes a PMT packet that names only the chosen video and audio stream (the first packet of a PMT that fits in one packet; false otherwise,
        // then the PMT goes on as it is). The program and its descriptors, the PCR PID and the version stay, the section length and the CRC are new.
        private bool RewritePmt(byte[] packet, byte[] result)
        {
            int start = 4;
            if ((packet[3] & 0x20) != 0)
                start += 1 + packet[4];   // adaptation field

            if ((packet[3] & 0x10) == 0 || start + 1 >= PacketSize)
                return false;

            int section = start + 1 + packet[start];   // pointer field
            if (section + 12 > PacketSize || packet[section] != 0x02)
                return false;

            int section_length = ((packet[section + 1] & 0x0F) << 8) | packet[section + 2];
            int end = section + 3 + section_length - 4;   // the CRC starts here
            if (end + 4 > PacketSize || end < section + 12)
                return false;

            int program_info = ((packet[section + 10] & 0x0F) << 8) | packet[section + 11];
            int loop = section + 12 + program_info;       // the streams start here
            if (loop > end)
                return false;

            // the new section starts after the 4 byte header and the pointer field
            int o = 5;
            Array.Copy(packet, section, result, o, loop - section);
            o += loop - section;

            int video_pid = _video_pid;
            int audio_pid = _audio_pid;
            for (int p = loop; p + 5 <= end; )
            {
                int stream_pid = ((packet[p + 1] & 0x1F) << 8) | packet[p + 2];
                int entry = 5 + (((packet[p + 3] & 0x0F) << 8) | packet[p + 4]);
                if (p + entry > end)
                    return false;   // damaged

                if (stream_pid > 0 && (stream_pid == video_pid || stream_pid == audio_pid))
                {
                    Array.Copy(packet, p, result, o, entry);
                    o += entry;
                }

                p += entry;
            }

            int new_length = (o - 5) - 3 + 4;   // behind the length field, with the CRC
            result[6] = (byte)((packet[section + 1] & 0xF0) | ((new_length >> 8) & 0x0F));
            result[7] = (byte)new_length;

            uint crc = Crc32(result, 5, o - 5);
            result[o++] = (byte)(crc >> 24);
            result[o++] = (byte)(crc >> 16);
            result[o++] = (byte)(crc >> 8);
            result[o++] = (byte)crc;

            result[0] = 0x47;
            result[1] = (byte)((packet[1] & 0x1F) | 0x40);   // payload start, no error flag
            result[2] = packet[2];
            result[3] = (byte)(0x10 | (packet[3] & 0x0F));   // payload only, the continuity counter goes on
            result[4] = 0x00;                                // pointer field
            for (int n = o; n < PacketSize; n++)
                result[n] = 0xFF;

            return true;
        }

        // CRC-32 of the MPEG-2 sections: polynomial 0x04C11DB7, start value 0xFFFFFFFF, not reflected, no final inversion
        private static uint Crc32(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
            {
                crc ^= (uint)data[i] << 24;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
            }

            return crc;
        }

        // Makes a packet that carries only the PCR of the packet (no payload, the rest of the adaptation field stuffed with 0xFF): the same PID
        // and the clock reference, nothing of the stream. false if the packet has no PCR. The continuity counter of such a packet does not count
        // (it has no payload), so it is left at 0.
        private static bool PcrOnly(byte[] packet, byte[] result)
        {
            if ((packet[3] & 0x20) == 0 || packet[4] < 7 || (packet[5] & 0x10) == 0)
                return false;

            result[0] = 0x47;
            result[1] = (byte)(packet[1] & 0x1F);   // no error flag, no payload start
            result[2] = packet[2];
            result[3] = 0x20;                        // adaptation field only
            result[4] = 183;
            result[5] = (byte)(packet[5] & 0xD0);    // discontinuity, random access and the PCR flag
            Array.Copy(packet, 6, result, 6, 6);     // the PCR
            for (int n = 12; n < PacketSize; n++)
                result[n] = 0xFF;

            return true;
        }

        private static int PidOf(byte[] packet)
        {
            return ((packet[1] & 0x1F) << 8) | packet[2];
        }

        private bool Accepted(byte[] packet)
        {
            int pid = PidOf(packet);

            return pid < 0x20 || pid == 0x1FFF || pid == _video_pid || pid == _audio_pid || _dynamic_pids[pid];
        }

        // reads the PAT (PMT PIDs) and the PMTs (PCR PID) of one packet
        private void Learnt(byte[] packet)
        {
            int pid = PidOf(packet);
            bool payload_start = (packet[1] & 0x40) != 0;
            if (!payload_start || (pid != 0 && !_dynamic_pids[pid]))
                return;

            int start = 4;
            if ((packet[3] & 0x20) != 0)
                start += 1 + packet[4];   // adaptation field

            if ((packet[3] & 0x10) == 0 || start + 1 >= PacketSize)
                return;

            int section = start + 1 + packet[start];   // pointer field
            if (section + 12 > PacketSize)
                return;

            int table_id = packet[section];
            int section_length = ((packet[section + 1] & 0x0F) << 8) | packet[section + 2];

            if (pid == 0 && table_id == 0x00)
            {
                // PAT: 4 bytes per program after the 8 byte header, up to the CRC; only a section that fits in the packet is read
                int end = section + 3 + section_length - 4;
                if (end > PacketSize)
                    return;

                var found = (bool[])_dynamic_pids.Clone();
                var pmts = new bool[PidCount];
                for (int p = section + 8; p + 4 <= end; p += 4)
                {
                    int program = (packet[p] << 8) | packet[p + 1];
                    int pmt_pid = ((packet[p + 2] & 0x1F) << 8) | packet[p + 3];
                    if (program != 0)
                    {
                        found[pmt_pid] = true;
                        pmts[pmt_pid] = true;
                    }
                }

                _pmt_pids = pmts;

                // keep the PCR PIDs that are known already
                for (int n = 0; n < PidCount; n++)
                    found[n] |= _pcr_pids[n];

                _dynamic_pids = found;
            }
            else if (pid != 0 && table_id == 0x02)
            {
                // PMT: the PCR PID follows the program number, the version and the section numbers
                int pcr_pid = ((packet[section + 8] & 0x1F) << 8) | packet[section + 9];

                // Most senders put the PCR on the video PID. That PID carries the video, so it must not pass just because it carries the PCR: it passes
                // as a PCR PID only if it is not one of the streams of the PMT (then it is the video or audio PID that decides; if that stream is not
                // chosen, only the clock reference of its packets goes on, see PcrOnly). Streams of the PMT:
                // 5 bytes each (type, PID, length of the descriptors) after the descriptors of the program, up to the CRC.
                bool is_stream = false;
                int end = section + 3 + section_length - 4;
                if (end <= PacketSize)
                {
                    int program_info = ((packet[section + 10] & 0x0F) << 8) | packet[section + 11];
                    for (int p = section + 12 + program_info; p + 5 <= end; )
                    {
                        if ((((packet[p + 1] & 0x1F) << 8) | packet[p + 2]) == pcr_pid)
                            is_stream = true;

                        p += 5 + (((packet[p + 3] & 0x0F) << 8) | packet[p + 4]);
                    }
                }

                if (pcr_pid != 0x1FFF)
                    _pcr_stream[pcr_pid] = is_stream;

                if (pcr_pid != 0x1FFF && _pcr_pids[pcr_pid] == is_stream)
                {
                    _pcr_pids[pcr_pid] = !is_stream;
                    _dynamic_pids[pcr_pid] = !is_stream;
                    Serilog.Log.Information("PID filter: PCR PID " + pcr_pid + (is_stream ? " is a stream of the PMT, it passes completely if it is the chosen video or audio PID, otherwise only its PCR" : " carries only the PCR, it always passes"));
                }
            }
        }
    }
}
