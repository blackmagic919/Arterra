using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Utils {
    public class LogicalBlockBuffer {
        private GraphicsBuffer _buffer;
        private uint2[] addressLL;
        public GraphicsBuffer Get() => _buffer;
        public void Destroy() => _buffer?.Release();
        public LogicalBlockBuffer(GraphicsBuffer.Target tg, int count, int stride) {
            this._buffer = new GraphicsBuffer(tg, count, stride);
            addressLL = new uint2[count + 1];
            addressLL[0].x = 1;
        }

        public uint Allocate() {
            uint addressIndex = addressLL[0].x;
            uint pAddress = addressLL[addressIndex].x;
            uint nAddress = addressLL[addressIndex].y == 0 ? addressLL[0].x + 1 : addressLL[addressIndex].y;
            addressLL[0].x = nAddress;

            addressLL[pAddress].y = nAddress;
            addressLL[nAddress].x = pAddress;
            return addressIndex;
        }

        public void Release(uint addressIndex) {
            if (addressIndex == 0) return;

            uint nAddress = addressLL[0].x;
            uint pAddress = addressLL[nAddress].x;
            addressLL[pAddress].y = addressIndex;
            addressLL[nAddress].x = addressIndex;
            addressLL[addressIndex] = new uint2(pAddress, nAddress);

            addressLL[0].x = addressIndex;
        }
    }
}