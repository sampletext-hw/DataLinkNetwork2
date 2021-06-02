using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DataLinkNetwork2.Abstractions;
using DataLinkNetwork2.BitArrayRoutine;
using DataLinkNetwork2.Checksum;

namespace DataLinkNetwork2.Communication
{
    public class Socket : ISocket
    {
        public MiddlewareBuffer _sendBuffer;
        public MiddlewareBuffer _receiveBuffer;

        private ISocket _pairedSocket;

        private bool _connected;
        private Mutex _connectedMutex;

        public event Action Connected;
        public event Action Disconnected;

        public event Action<byte[]> Received;

        public event Action StartedSending;
        public event Action StartedReceiving;

        private Queue<byte[]> _sendQueue;

        private AutoResetEvent _sendBarrier;

        private Thread _sendThread;
        private Thread _receiveThread;

        private volatile bool _terminate;
        
        public Socket()
        {
            _connectedMutex = new();
            _sendBarrier = new(false);
            _sendQueue = new();
        }

        private void SendRoutine()
        {
            StartedSending?.Invoke();
            while (!_terminate)
            {
                _sendBarrier.WaitOne();
                while (_sendQueue.Count > 0)
                {
                    PerformSend(_sendQueue.Dequeue());
                }
            }
        }

        private void PerformSend(byte[] array)
        {
            //Console.WriteLine("PerformSend");
            BitArray data = new BitArray(array).BitStaff();

            var arrays = data.Split(C.MaxFrameDataSize);

            for (var index = 0; index < arrays.Count; index++)
            {
                var dataBits = arrays[index];
                BitArray addressBits = new BitArray(C.AddressSize);
                BitArray controlBits = new BitArray(C.ControlSize);
                BitArrayWriter writer = new BitArrayWriter(controlBits);
                var controlBytes = new byte[] {(byte)(index & 0xFF), 0};
                writer.Write(new BitArray(controlBytes));

                Frame frame = new Frame(dataBits, addressBits, controlBits);

                var bitArray = frame.Build();
                //Console.WriteLine($"Sending Frame {index}");

                bool result = InternalSendFrame(bitArray, 0);
                if (!result)
                {
                    return;
                }
            }

            InternalSendEnd();
        }

        private bool InternalSendEnd()
        {
            //Console.WriteLine("InternalSendEnd");
            BitArray endAddressBits = new BitArray(C.AddressSize);
            BitArray endControlBits = new BitArray(C.ControlSize);
            BitArrayWriter endControlBitsWriter = new BitArrayWriter(endControlBits);
            endControlBitsWriter.Write(new BitArray(new byte[] {0, 0x11}));
            Frame endFrame = new Frame(new BitArray(0), endAddressBits, endControlBits);

            var endFrameBits = endFrame.Build();
            //Console.WriteLine("Sending End");
            return InternalSendFrame(endFrameBits, 0);
        }

        private bool InternalSendFrame(BitArray frameBits, int tried)
        {
            //Console.WriteLine("InternalSendFrame");
            _sendBuffer.Acquire();
            _sendBuffer.Push(frameBits);
            _sendBuffer.Release();

            var lastReceiveStatus = AwaitStatusCode();

            if (lastReceiveStatus == 1)
            {
                // Everything is OK
                _sendBuffer.ResetStatus();
                return true;
            }
            else if (lastReceiveStatus == -1)
            {
                _sendBuffer.ResetStatus();
                if (tried == 3)
                {
                    return false;
                }

                var result = InternalSendFrame(frameBits, tried + 1);
                return result;
            }

            return false;
        }

        private int AwaitStatusCode()
        {
            //Console.WriteLine("AwaitStatusCode");
            var stopwatch = Stopwatch.StartNew();

            int lastReceiveStatus = 0;

            while (lastReceiveStatus == 0)
            {
                lastReceiveStatus = _sendBuffer.GetStatusCode();
                if (lastReceiveStatus == 0)
                {
                    if (stopwatch.ElapsedMilliseconds > C.SendTimeoutMilliseconds)
                    {
                        stopwatch.Stop();
                        lastReceiveStatus = -1;
                        break;
                    }

                    Thread.Sleep(10);
                }
                else
                {
                }
            }

            return lastReceiveStatus;
        }

        private void ReceiveRoutine()
        {
            StartedReceiving?.Invoke();
            while (!_terminate)
            {
                if (_receiveBuffer.HasAvailable())
                {
                    var bytes = InternalReceive();

                    Received?.Invoke(bytes);
                }
                else
                {
                    Thread.Sleep(50);
                }
            }
        }

        private byte[] InternalReceive()
        {
            //Console.WriteLine("InternalReceive");
            _connectedMutex.WaitOne();
            Dictionary<int, byte[]> framedBytes = new();

            int lastReceived = -1;

            bool receivedEnd = false;

            while (!receivedEnd)
            {
                while (!_receiveBuffer.HasAvailable())
                {
                    Thread.Sleep(10);
                }

                //Console.WriteLine("Receiving Frame");
                var bitArray = InternalReceiveFrame();
                //Console.WriteLine("Received Frame");
                var frame = Frame.Parse(bitArray);
                BitArrayReader controlReader = new BitArrayReader(frame.Control);
                var controlBytes = controlReader.Read(16).ToByteArray();
                byte frameId = controlBytes[0];
                receivedEnd = controlBytes[1] == 0x11;

                if (receivedEnd)
                {
                    _receiveBuffer.SetStatusCode(1);
                    break;
                }

                if (lastReceived != byte.MaxValue && frameId <= lastReceived)
                {
                    _receiveBuffer.SetStatusCode(1);
                }
                else
                {
                    lastReceived = frameId;

                    var checksum = new VerticalOddityChecksumBuilder().Build(frame.Data);
                    if (frame.Checksum.IsSameNoCopy(checksum, 0, 0, C.ChecksumSize))
                    {
                        _receiveBuffer.SetStatusCode(1);
                        framedBytes.Add(frameId, frame.Data.DeBitStaff().ToByteArray());
                    }
                    else
                    {
                        _receiveBuffer.SetStatusCode(-1);
                    }
                }
            }

            Thread.Sleep(50);

            var total = framedBytes.OrderBy(f => f.Key).SelectMany(b => b.Value).ToList();
            // if (_receiveBuffer.HasAvailable())
            // {
            //     total.AddRange(InternalReceive());
            // }

            _connectedMutex.ReleaseMutex();
            return total.ToArray();
        }

        private BitArray InternalReceiveFrame()
        {
            //Console.WriteLine("InternalReceiveFrame");
            _receiveBuffer.Acquire();
            var bitArray = _receiveBuffer.Get();
            _receiveBuffer.Release();

            return bitArray;
        }

        public void Send(byte[] array)
        {
            _sendQueue.Enqueue(array);

            if (_sendQueue.Count == 1)
            {
                _sendBarrier.Set();
            }
        }

        public void Connect(ISocket socket)
        {
            if (!_connected)
            {
                // Accept already inversed buffers order
                (_sendBuffer, _receiveBuffer) = socket.AcceptConnect(this);
                _connectedMutex.WaitOne();
                _pairedSocket = socket;
                _connected = true;
                _terminate = false;
                _sendThread = new Thread(SendRoutine);
                _sendThread.Start();
                _receiveThread = new Thread(ReceiveRoutine);
                _receiveThread.Start();
                Connected?.Invoke();
                _connectedMutex.ReleaseMutex();
            }
        }

        public void Disconnect()
        {
            if (_connected)
            {
                _pairedSocket.AcceptDisconnect();
                _connected = false;
                _terminate = true;
                Disconnected?.Invoke();
            }
        }

        public (MiddlewareBuffer, MiddlewareBuffer) AcceptConnect(ISocket socket)
        {
            _connectedMutex.WaitOne();
            _receiveBuffer = new MiddlewareBuffer();
            _sendBuffer = new MiddlewareBuffer();
            _connected = true;

            _pairedSocket = socket;

            _terminate = false;
            
            _sendThread = new Thread(SendRoutine);
            _sendThread.Start();
            _receiveThread = new Thread(ReceiveRoutine);
            _receiveThread.Start();

            Connected?.Invoke();

            _connectedMutex.ReleaseMutex();
            // Inverse buffer order
            return (_receiveBuffer, _sendBuffer);
        }

        public void AcceptDisconnect()
        {
            _connectedMutex.WaitOne();
            _connected = false;
            _pairedSocket = null;

            _terminate = true;

            Disconnected?.Invoke();
            _connectedMutex.ReleaseMutex();
        }
    }
}