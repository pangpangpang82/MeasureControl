using System;
using System.Collections.Generic;

namespace MeasureControl.Simulations.Common
{
    public sealed class MultiLabelCommandAssembler
    {
        private readonly ushort[] _parts = new ushort[4];
        private int _mask;
        private DateTime _firstSeenUtc;
        private readonly Dictionary<byte, int> _labelToIndex;

        private static readonly TimeSpan AssemblyTimeout = TimeSpan.FromMilliseconds(200);

        public MultiLabelCommandAssembler(byte[] fragLabels)
        {
            _labelToIndex = new Dictionary<byte, int>();
            if (fragLabels != null)
            {
                for (int i = 0; i < fragLabels.Length && i < 4; i++)
                {
                    _labelToIndex[fragLabels[i]] = i;
                }
            }
        }

        public bool TryAddFragment(byte label, ushort payload16, DateTime nowUtc, out byte[] cmd8)
        {
            cmd8 = null;

            if (!_labelToIndex.TryGetValue(label, out var index))
                return false;

            if (_mask == 0 || (nowUtc - _firstSeenUtc) > AssemblyTimeout)
            {
                _mask = 0;
                _firstSeenUtc = nowUtc;
            }

            _parts[index] = payload16;
            _mask |= (1 << index);

            if (_mask != 0b1111)
                return false;

            cmd8 = new byte[8];
            for (int j = 0; j < 4; j++)
            {
                cmd8[j * 2] = (byte)((_parts[j] >> 8) & 0xFF);
                cmd8[j * 2 + 1] = (byte)(_parts[j] & 0xFF);
            }

            _mask = 0;
            return true;
        }

        public void Reset()
        {
            _mask = 0;
            _firstSeenUtc = default;
        }
    }
}
