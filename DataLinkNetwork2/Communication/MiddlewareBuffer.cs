using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace DataLinkNetwork2.Communication
{
    public class MiddlewareBuffer
    {
        private readonly Queue<BitArray> _dataQueue;

        private Response _response;

        private readonly Mutex _acquireMutex;

        public MiddlewareBuffer()
        {
            _acquireMutex = new();
            _dataQueue = new Queue<BitArray>();
        }

        public bool HasAvailable()
        {
            return _dataQueue.Count > 0;
        }

        public void Acquire()
        {
            _acquireMutex.WaitOne();
        }
        
        public void Release()
        {
            _acquireMutex.ReleaseMutex();
        }

        public BitArray Get()
        {
            var bitArray = _dataQueue.Dequeue();
            return bitArray;
        }
        
        public void Push(BitArray data)
        {
            _dataQueue.Enqueue(data);
        }

        public void SetResponse(Response response)
        {
            _response = response;
        }
        
        public Response GetStatusCode()
        {
            return _response;
        }

        public void ResetStatus()
        {
            _response = Response.Undefined;
        }
    }
}