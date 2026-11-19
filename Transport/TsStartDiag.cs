using Serilog;
using System;
using System.Text;

namespace opentuner
{
    // Test diagnostics (branch h265-start-diagnostics, not for a release): what the first seconds of every TS start look like.
    // A "start" is the first data after a pause of 400 ms or more (the TS thread is stopped on every tune and started at the lock).
    // One line per tuner at 4 s (and at 15 s if no SPS was seen yet, Information level, --debuglevel 2):
    // the first byte of the data (0x47 = aligned), skipped bytes before the first sync, when PAT, the first video PES and the
    // parameter sets / key frames of the video (H.265 and H.264 NAL units) arrived, and the error counters of this start only.
    public class TsStartDiag
    {
        private const long NewStartGapMs = 400;
        private const long FirstSummaryMs = 4000;
        private const long LateSummaryMs = 15000;

        private readonly string _name;
        private readonly TSAnalyzer _analyzer;

        private bool _active;
        private long _startMs;
        private long _lastActivityMs;
        private bool _summaryDone;
        private bool _lateSummaryDone;
        private bool _spsSeenAtFirstSummary;

        private int _firstByte;                 // -1 = no data yet
        private int _skippedBytes;
        private int _skipEpisodes;
        private bool _inSkip;
        private long _syncMs;
        private long _patMs;
        private long _videoPesMs;
        private int _videoPid;
        private int _packets;
        private int _cc, _tei, _enc;

        // PTS minus PCR of the video PES (ms): the buffer the sender demands from every decoder before a picture may be shown
        private long _lastPcr;                  // 90 kHz, -1 = none yet
        private int _pcrPid;
        private long _ptsPcrSum, _ptsPcrMin, _ptsPcrMax;
        private int _ptsPcrCount;

        // first time per NAL unit type (ms after the start of the data) and the count, H.265 (type = (b >> 1) & 0x3F) and H.264 (type = b & 0x1F)
        private readonly long[] _hevcFirst = new long[64];
        private readonly int[] _hevcCount = new int[64];
        private readonly long[] _avcFirst = new long[32];
        private readonly int[] _avcCount = new int[32];

        // scan state over the video payload bytes: 00 00 01 is followed by the NAL header byte
        private int _zeros;
        private bool _expectNalHeader;

        public TsStartDiag(string name, TSAnalyzer analyzer)
        {
            _name = name;
            _analyzer = analyzer;
        }

        public void OnSkippedByte(byte value)
        {
            Touch();
            if (_firstByte < 0)
                _firstByte = value;

            _skippedBytes++;
            if (!_inSkip)
            {
                _inSkip = true;
                _skipEpisodes++;
            }
        }

        public void OnPacket(byte[] packet, uint pid)
        {
            long now = Touch();
            long ms = now - _startMs;

            if (_firstByte < 0)
                _firstByte = packet[0];

            _inSkip = false;
            _packets++;

            if (_syncMs < 0)
                _syncMs = ms;

            if (pid == 0 && _patMs < 0)
                _patMs = ms;

            ReadPcr(packet, pid);
            ScanVideo(packet, pid, ms);

            if (!_summaryDone && ms >= FirstSummaryMs)
            {
                _spsSeenAtFirstSummary = SpsSeen();
                Summarize("+4 s");
                _summaryDone = true;
            }
            else if (_summaryDone && !_lateSummaryDone && ms >= LateSummaryMs)
            {
                _lateSummaryDone = true;
                if (!_spsSeenAtFirstSummary)
                    Summarize("+15 s");
            }
        }

        public void OnContinuityError() { if (_active) _cc++; }
        public void OnTransportError() { if (_active) _tei++; }
        public void OnScrambled() { if (_active) _enc++; }

        // starts a new start of the data after a pause (the previous one is summarized first), returns the current time in ms
        private long Touch()
        {
            long now = Environment.TickCount64;

            if (!_active || now - _lastActivityMs > NewStartGapMs)
            {
                if (_active && !_summaryDone)
                    Summarize("ended after " + (_lastActivityMs - _startMs) + " ms");

                Reset(now);
            }

            _lastActivityMs = now;
            return now;
        }

        private void Reset(long now)
        {
            _active = true;
            _startMs = now;
            _summaryDone = false;
            _lateSummaryDone = false;
            _spsSeenAtFirstSummary = false;
            _firstByte = -1;
            _skippedBytes = 0;
            _skipEpisodes = 0;
            _inSkip = false;
            _syncMs = -1;
            _patMs = -1;
            _videoPesMs = -1;
            _videoPid = -1;
            _packets = 0;
            _cc = _tei = _enc = 0;
            _lastPcr = -1;
            _pcrPid = -1;
            _ptsPcrSum = 0;
            _ptsPcrMin = long.MaxValue;
            _ptsPcrMax = long.MinValue;
            _ptsPcrCount = 0;
            Array.Fill(_hevcFirst, -1L);
            Array.Clear(_hevcCount, 0, _hevcCount.Length);
            Array.Fill(_avcFirst, -1L);
            Array.Clear(_avcCount, 0, _avcCount.Length);
            _zeros = 0;
            _expectNalHeader = false;
        }

        // PCR base (90 kHz) of the first PID that carries a PCR in its adaptation field
        private void ReadPcr(byte[] packet, uint pid)
        {
            if ((packet[3] & 0x20) == 0 || packet[4] < 7 || (packet[5] & 0x10) == 0)
                return;

            if (_pcrPid < 0)
                _pcrPid = (int)pid;
            if (pid != _pcrPid)
                return;

            _lastPcr = ((long)packet[6] << 25) | ((long)packet[7] << 17) | ((long)packet[8] << 9) | ((long)packet[9] << 1) | ((long)packet[10] >> 7);
        }

        // PTS of a video PES that starts in this packet minus the last PCR, in ms (33 bit wrap handled)
        private void ReadPts(byte[] packet, int offset)
        {
            if (_lastPcr < 0 || offset + 14 > TSParserThread.TS_PACKET_SIZE || (packet[offset + 7] & 0x80) == 0)
                return;

            long pts = ((long)(packet[offset + 9] >> 1) & 0x07) << 30 | ((long)packet[offset + 10] << 22) | ((long)(packet[offset + 11] >> 1) << 15)
                       | ((long)packet[offset + 12] << 7) | ((long)packet[offset + 13] >> 1);

            const long Wrap = 1L << 33;
            long delta = (pts - _lastPcr) & (Wrap - 1);
            if (delta >= Wrap / 2)
                delta -= Wrap;

            long delta_ms = delta / 90;

            // a damaged packet (false PCR or PTS after a lost sync) gives nonsense: a real value is 0 to a few seconds
            if (delta_ms < -1000 || delta_ms > 30000)
                return;

            _ptsPcrSum += delta_ms;
            _ptsPcrMin = Math.Min(_ptsPcrMin, delta_ms);
            _ptsPcrMax = Math.Max(_ptsPcrMax, delta_ms);
            _ptsPcrCount++;
        }

        private void ScanVideo(byte[] packet, uint pid, long ms)
        {
            if (ms > LateSummaryMs || (packet[3] & 0x10) == 0)
                return;

            int offset = 4;
            if ((packet[3] & 0x20) != 0)
                offset += 1 + packet[4];
            if (offset >= TSParserThread.TS_PACKET_SIZE)
                return;

            // the first PES with a video stream id (0xE0 - 0xEF) names the video PID
            if (_videoPid < 0 && (packet[1] & 0x40) != 0 && offset + 4 <= TSParserThread.TS_PACKET_SIZE
                && packet[offset] == 0 && packet[offset + 1] == 0 && packet[offset + 2] == 1 && (packet[offset + 3] & 0xF0) == 0xE0)
            {
                _videoPid = (int)pid;
                _videoPesMs = ms;
            }

            if (pid != _videoPid)
                return;

            if ((packet[1] & 0x40) != 0 && offset + 4 <= TSParserThread.TS_PACKET_SIZE
                && packet[offset] == 0 && packet[offset + 1] == 0 && packet[offset + 2] == 1 && (packet[offset + 3] & 0xF0) == 0xE0)
                ReadPts(packet, offset);

            for (int i = offset; i < TSParserThread.TS_PACKET_SIZE; i++)
            {
                byte b = packet[i];

                if (_expectNalHeader)
                {
                    _expectNalHeader = false;
                    _zeros = 0;

                    int hevc = (b >> 1) & 0x3F;
                    int avc = b & 0x1F;
                    if (_hevcFirst[hevc] < 0) _hevcFirst[hevc] = ms;
                    _hevcCount[hevc]++;
                    if (_avcFirst[avc] < 0) _avcFirst[avc] = ms;
                    _avcCount[avc]++;
                    continue;
                }

                if (b == 0)
                {
                    _zeros++;
                }
                else
                {
                    if (b == 1 && _zeros >= 2)
                        _expectNalHeader = true;
                    _zeros = 0;
                }
            }
        }

        // The codec comes from the NAL units found in the video (SPS + PPS of H.265 or H.264), not from the PMT: right after a
        // tune the analyzer can still hold the PMT of the previous service (same PID, other stream type), which gave wrong lines.
        private bool HevcSeen()
        {
            return _hevcFirst[33] >= 0 && _hevcFirst[34] >= 0;
        }

        private bool AvcSeen()
        {
            return _avcFirst[7] >= 0 && _avcFirst[8] >= 0;
        }

        // 265, 264 or 0 = no SPS + PPS seen yet
        private int Codec()
        {
            bool hevc = HevcSeen();
            bool avc = AvcSeen();

            if (hevc && !avc) return 265;
            if (avc && !hevc) return 264;
            if (hevc && avc) return _videoPid >= 0 && _analyzer.StreamTypeOf((uint)_videoPid) == 0x1B ? 264 : 265;
            return 0;
        }

        private bool SpsSeen()
        {
            return HevcSeen() || AvcSeen();
        }

        private static string At(long ms)
        {
            return ms < 0 ? "-" : "+" + ms + " ms";
        }

        private int IrapCount()
        {
            int count = 0;
            for (int t = 16; t <= 21; t++)
                count += _hevcCount[t];
            return count;
        }

        private long IrapFirst()
        {
            long first = -1;
            for (int t = 16; t <= 21; t++)
            {
                if (_hevcFirst[t] >= 0 && (first < 0 || _hevcFirst[t] < first))
                    first = _hevcFirst[t];
            }
            return first;
        }

        private long SliceFirst()
        {
            long first = -1;
            for (int t = 0; t <= 9; t++)
            {
                if (_hevcFirst[t] >= 0 && (first < 0 || _hevcFirst[t] < first))
                    first = _hevcFirst[t];
            }
            return first;
        }

        private void Summarize(string when)
        {
            var sb = new StringBuilder();
            sb.Append("TS start ").Append(_name).Append(" (").Append(when).Append("): first byte ")
              .Append(_firstByte < 0 ? "none" : "0x" + _firstByte.ToString("X2"))
              .Append(", skipped ").Append(_skippedBytes).Append(" bytes in ").Append(_skipEpisodes).Append(" run(s), first sync ").Append(At(_syncMs))
              .Append(", PAT ").Append(At(_patMs))
              .Append(", video PES ").Append(At(_videoPesMs));

            if (_videoPid >= 0)
            {
                sb.Append(" (PID 0x").Append(_videoPid.ToString("X4")).Append(", stream type 0x")
                  .Append(Math.Max(0, (int)_analyzer.StreamTypeOf((uint)_videoPid)).ToString("X2")).Append(")");
            }

            int codec = Codec();

            if (codec == 264)
            {
                sb.Append(", H.264: SPS ").Append(At(_avcFirst[7])).Append(", PPS ").Append(At(_avcFirst[8]))
                  .Append(", first IDR ").Append(At(_avcFirst[5])).Append(" (").Append(_avcCount[5]).Append(")");
            }
            else if (codec == 0)
            {
                sb.Append(", no SPS + PPS of H.265 or H.264 found yet");
            }
            else
            {
                sb.Append(", H.265: VPS ").Append(At(_hevcFirst[32])).Append(", SPS ").Append(At(_hevcFirst[33]))
                  .Append(", PPS ").Append(At(_hevcFirst[34]))
                  .Append(", first slice ").Append(At(SliceFirst()))
                  .Append(", first IRAP ").Append(At(IrapFirst())).Append(" (").Append(IrapCount()).Append(" IRAP, ").Append(_hevcCount[32]).Append(" VPS, ")
                  .Append(_hevcCount[33]).Append(" SPS)");
            }

            if (_ptsPcrCount > 0)
            {
                sb.Append(", PTS-PCR ").Append(_ptsPcrSum / _ptsPcrCount).Append(" ms avg (").Append(_ptsPcrMin).Append(" to ").Append(_ptsPcrMax)
                  .Append(", ").Append(_ptsPcrCount).Append(" frames)");
            }
            else
            {
                sb.Append(", PTS-PCR -");
            }

            sb.Append(", CC ").Append(_cc).Append(" TEI ").Append(_tei).Append(" ENC ").Append(_enc).Append(", packets ").Append(_packets);

            Log.Information(sb.ToString());
        }
    }
}
