﻿namespace OpenTl.ClientApi.MtProto.Layers.Tcp
{
    using System.Threading.Tasks;

    using BarsGroup.CodeGuard;

    using DotNetty.Buffers;
    using DotNetty.Transport.Channels;

    using log4net;

    using Crc32 = OpenTl.Common.Crypto.Crc32;

    internal sealed class TcpLayerHandlerAdapter: ChannelHandlerAdapter
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(TcpLayerHandlerAdapter));

        private int _sequenceNumber;
        
        public override void ChannelActive(IChannelHandlerContext context)
        {
            _sequenceNumber = 0;
            
            base.ChannelActive(context);
        }

        public override  Task WriteAsync(IChannelHandlerContext context, object message)
        {
            var sequenceNumer = _sequenceNumber++;

            var inputBuffer = (IByteBuffer)message;

            // https://core.telegram.org/mtproto#tcp-transport
            /*
                4 length bytes are added at the front 
                (to include the length, the sequence number, and CRC32; always divisible by 4)
                and 4 bytes with the packet sequence number within this TCP connection 
                (the first packet sent is numbered 0, the next one 1, etc.),
                and 4 CRC32 bytes at the end (length, sequence number, and payload together).
            */
            
            var dataLength = inputBuffer.ReadableBytes + 4 + 4;
            var packageLength = dataLength + 4;
            var buffer = PooledByteBufferAllocator.Default.Buffer(dataLength, packageLength);

            buffer.WriteIntLE(packageLength);
            buffer.WriteIntLE(sequenceNumer);
            buffer.WriteBytes(inputBuffer);

            var data = new byte[dataLength];
            buffer.GetBytes(0, data);
            
            buffer.WriteIntLE((int)Crc32.Compute(data));

            Log.Debug($"Send the message with sequence number {sequenceNumer}");

            return context.WriteAsync(buffer);
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            if (message is EmptyByteBuffer || !(message is IByteBuffer input))
            {
                return;
            }

            var length = input.ReadIntLE();
            var packageLength = length - 4;
            var dataLength = packageLength - 4 - 4;

            var sequenceNumber = input.ReadIntLE();

            var data = input.ReadBytes(dataLength);

            CheckChecksum(input, packageLength);
            
            Log.Debug($"Recieve the message with sequence number {sequenceNumber}");

            if (dataLength == 4)
            {
                var code = data.ReadIntLE();
                Log.Error($"Recieve the message with {code}");
            }
            
            context.FireChannelRead(data);
        }
        
        private static void CheckChecksum(IByteBuffer buffer, int length)
        {
            buffer.ResetReaderIndex();

            var checksumData = new byte[length];
            var checksumBuffer = buffer.ReadBytes(checksumData);
            
            var checksum = buffer.ReadUnsignedIntLE();
            
            var computeChecksum = Crc32.Compute(checksumData);
            
            Guard.That(computeChecksum).IsEqual(checksum);
        }
    }
}