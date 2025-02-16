using System.Runtime.CompilerServices;
using Windows.Storage.Streams;
using IRIS.Data;

namespace IRIS.Bluetooth.Utility
{
    /// <summary>
    /// Extensions for <see cref="IBuffer"/>
    /// </summary>
    public static class BufferExtensions
    {
        /// <summary>
        /// Write data to buffer
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IBuffer? ToBuffer<TBufferData>(this TBufferData bufferData)
        {
            using DataWriter writer = new();
            switch (bufferData)
            {
                case byte[] data: writer.WriteBytes(data); break;
                case string data: writer.WriteString(data); break;
                case bool data: writer.WriteBoolean(data); break;
                case byte data: writer.WriteByte(data); break;
                case short data: writer.WriteInt16(data); break;
                case ushort data: writer.WriteUInt16(data); break;
                case int data: writer.WriteInt32(data); break;
                case uint data: writer.WriteUInt32(data); break;
                case long data: writer.WriteInt64(data); break;
                case ulong data: writer.WriteUInt64(data); break;
                case float data: writer.WriteSingle(data); break;
                case double data: writer.WriteDouble(data); break;
                case TimeSpan data: writer.WriteTimeSpan(data); break;
                case Guid data: writer.WriteGuid(data); break;
                case DateTime data: writer.WriteDateTime(data); break;
                default: return null;
            }

            return writer.DetachBuffer();
        }

        /// <summary>
        /// Read data from buffer
        /// </summary>
        /// <param name="buffer">Buffer</param>
        /// <typeparam name="TBufferData">Type of data</typeparam>
        /// <returns>Read data or null if type is not supported</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DataPromise<TBufferData> Read<TBufferData>(this IBuffer buffer)
        {
            // Create new reader
            using DataReader reader = DataReader.FromBuffer(buffer);

            object? result = null;

            if (typeof(TBufferData) == typeof(byte[]))
            {
                // Allocate new buffer and copy data
                byte[] data = new byte[buffer.Length];
                reader.ReadBytes(data);
                result = data;
            }
            else if (typeof(TBufferData) == typeof(string))
            {
                result = reader.ReadString(buffer.Length);
            }

            // Numbers
            else if (typeof(TBufferData) == typeof(bool))
                result = reader.ReadBoolean();
            else if (typeof(TBufferData) == typeof(byte))
                result = reader.ReadByte();
            else if (typeof(TBufferData) == typeof(short))
                result = reader.ReadInt16();
            else if (typeof(TBufferData) == typeof(ushort))
                result = reader.ReadUInt16();
            else if (typeof(TBufferData) == typeof(int))
                result = reader.ReadInt32();
            else if (typeof(TBufferData) == typeof(uint))
                result = reader.ReadUInt32();
            else if (typeof(TBufferData) == typeof(long))
                result = reader.ReadInt64();
            else if (typeof(TBufferData) == typeof(ulong))
                result = reader.ReadUInt64();
            else if (typeof(TBufferData) == typeof(float))
                result = reader.ReadSingle();
            else if (typeof(TBufferData) == typeof(double))
                result = reader.ReadDouble();

            // Objects
            else if (typeof(TBufferData) == typeof(TimeSpan))
                result = reader.ReadTimeSpan();
            else if (typeof(TBufferData) == typeof(Guid))
                result = reader.ReadGuid();
            else if (typeof(TBufferData) == typeof(DateTime)) result = reader.ReadDateTime();

            // Check if result is valid type, if so return it, if not return null
            if (result is TBufferData properResult) return DataPromise.FromSuccess(properResult);
            return DataPromise.FromFailure<TBufferData>();
        }
    }
}