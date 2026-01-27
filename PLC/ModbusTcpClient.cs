using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Automation
{
    public sealed class ModbusTcpClient : IDisposable
    {
        private TcpClient client;
        private NetworkStream stream;
        private ushort transactionId;

        public bool IsConnected => client != null && client.Connected;

        public void Connect(string ip, int port, int timeoutMs)
        {
            Disconnect();
            client = new TcpClient();
            client.ReceiveTimeout = timeoutMs;
            client.SendTimeout = timeoutMs;
            IAsyncResult result = client.BeginConnect(ip, port, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(timeoutMs);
            if (!success || !client.Connected)
            {
                client.Close();
                client = null;
                throw new IOException("Modbus TCP连接失败");
            }
            client.EndConnect(result);
            stream = client.GetStream();
        }

        public void Disconnect()
        {
            if (stream != null)
            {
                stream.Close();
                stream = null;
            }
            if (client != null)
            {
                client.Close();
                client = null;
            }
        }

        public byte[] ReadCoils(byte unitId, ushort address, ushort quantity)
        {
            return ReadBits(unitId, 0x01, address, quantity);
        }

        public byte[] ReadDiscreteInputs(byte unitId, ushort address, ushort quantity)
        {
            return ReadBits(unitId, 0x02, address, quantity);
        }

        public ushort[] ReadHoldingRegisters(byte unitId, ushort address, ushort quantity)
        {
            return ReadRegisters(unitId, 0x03, address, quantity);
        }

        public ushort[] ReadInputRegisters(byte unitId, ushort address, ushort quantity)
        {
            return ReadRegisters(unitId, 0x04, address, quantity);
        }

        public void WriteSingleCoil(byte unitId, ushort address, bool value)
        {
            byte[] data = new byte[4];
            WriteUInt16(data, 0, address);
            WriteUInt16(data, 2, value ? (ushort)0xFF00 : (ushort)0x0000);
            byte[] response = SendRequest(unitId, 0x05, data);
            ValidateWriteResponse(response, 0x05);
        }

        public void WriteMultipleCoils(byte unitId, ushort address, bool[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("写入线圈数量为空");
            }
            ushort quantity = (ushort)values.Length;
            int byteCount = (values.Length + 7) / 8;
            byte[] data = new byte[5 + byteCount];
            WriteUInt16(data, 0, address);
            WriteUInt16(data, 2, quantity);
            data[4] = (byte)byteCount;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                {
                    data[5 + (i / 8)] |= (byte)(1 << (i % 8));
                }
            }
            byte[] response = SendRequest(unitId, 0x0F, data);
            ValidateWriteResponse(response, 0x0F);
        }

        public void WriteSingleRegister(byte unitId, ushort address, ushort value)
        {
            byte[] data = new byte[4];
            WriteUInt16(data, 0, address);
            WriteUInt16(data, 2, value);
            byte[] response = SendRequest(unitId, 0x06, data);
            ValidateWriteResponse(response, 0x06);
        }

        public void WriteMultipleRegisters(byte unitId, ushort address, ushort[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("写入寄存器数量为空");
            }
            ushort quantity = (ushort)values.Length;
            byte[] data = new byte[5 + values.Length * 2];
            WriteUInt16(data, 0, address);
            WriteUInt16(data, 2, quantity);
            data[4] = (byte)(values.Length * 2);
            for (int i = 0; i < values.Length; i++)
            {
                WriteUInt16(data, 5 + i * 2, values[i]);
            }
            byte[] response = SendRequest(unitId, 0x10, data);
            ValidateWriteResponse(response, 0x10);
        }

        private byte[] ReadBits(byte unitId, byte function, ushort address, ushort quantity)
        {
            byte[] data = new byte[4];
            WriteUInt16(data, 0, address);
            WriteUInt16(data, 2, quantity);
            byte[] response = SendRequest(unitId, function, data);
            ValidateReadResponse(response, function, out byte[] payload);
            if (payload.Length < 1)
            {
                throw new IOException("Modbus响应数据长度无效");
            }
            int byteCount = payload[0];
            if (payload.Length != 1 + byteCount)
            {
                throw new IOException("Modbus响应字节数不匹配");
            }
            byte[] bits = new byte[quantity];
            for (int i = 0; i < quantity; i++)
            {
                int byteIndex = i / 8;
                int bitIndex = i % 8;
                if (byteIndex >= byteCount)
                {
                    break;
                }
                bits[i] = (byte)(((payload[1 + byteIndex] >> bitIndex) & 0x01) == 1 ? 1 : 0);
            }
            return bits;
        }

        private ushort[] ReadRegisters(byte unitId, byte function, ushort address, ushort quantity)
        {
            byte[] data = new byte[4];
            WriteUInt16(data, 0, address);
            WriteUInt16(data, 2, quantity);
            byte[] response = SendRequest(unitId, function, data);
            ValidateReadResponse(response, function, out byte[] payload);
            if (payload.Length < 1)
            {
                throw new IOException("Modbus响应数据长度无效");
            }
            int byteCount = payload[0];
            if (byteCount != quantity * 2 || payload.Length != 1 + byteCount)
            {
                throw new IOException("Modbus响应寄存器长度不匹配");
            }
            ushort[] registers = new ushort[quantity];
            for (int i = 0; i < quantity; i++)
            {
                registers[i] = ReadUInt16(payload, 1 + i * 2);
            }
            return registers;
        }

        private byte[] SendRequest(byte unitId, byte function, byte[] data)
        {
            if (stream == null)
            {
                throw new IOException("Modbus TCP未连接");
            }
            ushort tid = unchecked(++transactionId);
            ushort length = (ushort)(1 + 1 + (data?.Length ?? 0));
            byte[] header = new byte[7];
            WriteUInt16(header, 0, tid);
            WriteUInt16(header, 2, 0);
            WriteUInt16(header, 4, length);
            header[6] = unitId;

            int pduLength = 1 + (data?.Length ?? 0);
            byte[] buffer = new byte[7 + pduLength];
            Buffer.BlockCopy(header, 0, buffer, 0, 7);
            buffer[7] = function;
            if (data != null && data.Length > 0)
            {
                Buffer.BlockCopy(data, 0, buffer, 8, data.Length);
            }

            stream.Write(buffer, 0, buffer.Length);
            return ReadResponse(tid);
        }

        private byte[] ReadResponse(ushort expectedTid)
        {
            byte[] header = ReadExact(7);
            ushort tid = ReadUInt16(header, 0);
            if (tid != expectedTid)
            {
                throw new IOException("Modbus事务号不匹配");
            }
            ushort protocol = ReadUInt16(header, 2);
            if (protocol != 0)
            {
                throw new IOException("Modbus协议号无效");
            }
            ushort length = ReadUInt16(header, 4);
            if (length <= 1)
            {
                throw new IOException("Modbus响应长度无效");
            }
            int pduLength = length - 1;
            byte[] pdu = ReadExact(pduLength);
            byte[] response = new byte[header.Length + pdu.Length];
            Buffer.BlockCopy(header, 0, response, 0, header.Length);
            Buffer.BlockCopy(pdu, 0, response, header.Length, pdu.Length);
            return response;
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    throw new IOException("Modbus TCP连接中断");
                }
                offset += read;
            }
            return buffer;
        }

        private static void ValidateReadResponse(byte[] response, byte function, out byte[] payload)
        {
            if (response == null || response.Length < 9)
            {
                throw new IOException("Modbus响应长度无效");
            }
            byte respFunction = response[7];
            if ((respFunction & 0x80) != 0)
            {
                byte code = response[8];
                throw new IOException($"Modbus异常码:{code}");
            }
            if (respFunction != function)
            {
                throw new IOException("Modbus功能码不匹配");
            }
            payload = new byte[response.Length - 8];
            Buffer.BlockCopy(response, 8, payload, 0, payload.Length);
        }

        private static void ValidateWriteResponse(byte[] response, byte function)
        {
            if (response == null || response.Length < 12)
            {
                throw new IOException("Modbus写响应长度无效");
            }
            byte respFunction = response[7];
            if ((respFunction & 0x80) != 0)
            {
                byte code = response[8];
                throw new IOException($"Modbus异常码:{code}");
            }
            if (respFunction != function)
            {
                throw new IOException("Modbus功能码不匹配");
            }
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 1] = (byte)(value & 0xFF);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
