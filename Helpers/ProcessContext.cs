using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MapAssist.Helpers
{
    public class ProcessContext : IDisposable
    {
        public int OpenContextCount = 1;
        private static readonly NLog.Logger _log = NLog.LogManager.GetCurrentClassLogger();
        private Process _process;
        private IntPtr _handle;
        private IntPtr _baseAddr;
        private int _moduleSize;
        private bool _disposedValue;

        public ProcessContext(Process process)
        {
            _process = process;
            _handle = WindowsExternal.OpenProcess((uint)WindowsExternal.ProcessAccessFlags.VirtualMemoryRead, false, _process.Id);
            _baseAddr = _process.MainModule.BaseAddress;
            _moduleSize = _process.MainModule.ModuleMemorySize;
        }

        public IntPtr Handle => _handle;
        public IntPtr BaseAddr => _baseAddr;
        public int ModuleSize => _moduleSize;
        public int ProcessId => _process.Id;

        public IntPtr GetUnitHashtableOffsetNEW(byte[] buffer)
        {
            var pattern = new Pattern("00 C8 01 00 90 03 00 00");
            var patternAddress = FindPattern(buffer, pattern);
            if (patternAddress == IntPtr.Zero) return IntPtr.Zero;

            var offsetBuffer = new byte[4];
            var resultRelativeAddress = IntPtr.Add(patternAddress, 7);
            if (!WindowsExternal.ReadProcessMemory(_handle, resultRelativeAddress, offsetBuffer, sizeof(int), out _))
            {
                _log.Info($"Failed to find pattern {pattern}");
                return IntPtr.Zero;
            }

            return IntPtr.Add(_baseAddr, BitConverter.ToInt32(offsetBuffer, 0));
        }

        public IntPtr GetUnitHashtableOffset(byte[] buffer)
        {
            var pattern = new Pattern("48 03 C7 49 8B 8C C6");
            var patternAddress = FindPattern(buffer, pattern);
            if (patternAddress == IntPtr.Zero) return IntPtr.Zero;

            var offsetBuffer = new byte[4];
            var resultRelativeAddress = IntPtr.Add(patternAddress, 7);
            if (!WindowsExternal.ReadProcessMemory(_handle, resultRelativeAddress, offsetBuffer, sizeof(int), out _))
            {
                _log.Info($"Failed to find pattern {pattern}");
                return IntPtr.Zero;
            }

            return IntPtr.Add(_baseAddr, BitConverter.ToInt32(offsetBuffer, 0));
        }

        public IntPtr GetExpansionOffset(byte[] buffer)
        {
            var pattern = new Pattern("48 8B 05 ? ? ? ? 48 8B D9 F3 0F 10 50");
            var patternAddress = FindPattern(buffer, pattern);
            if (patternAddress == IntPtr.Zero) return IntPtr.Zero;

            var offsetBuffer = new byte[4];
            if (!WindowsExternal.ReadProcessMemory(_handle, IntPtr.Add(patternAddress, 3), offsetBuffer, sizeof(int), out _))
            {
                _log.Info($"Failed to find pattern {pattern}");
                return IntPtr.Zero;
            }

            var displacement = BitConverter.ToInt32(offsetBuffer, 0);
            return IntPtr.Add(patternAddress, 7 + displacement);
        }

        public IntPtr GetGameNameOffset(byte[] buffer)
        {
            var pattern = new Pattern("44 88 25 ? ? ? ? 66 44 89 25");
            var patternAddress = FindPattern(buffer, pattern);
            if (patternAddress == IntPtr.Zero) return IntPtr.Zero;

            var offsetBuffer = new byte[4];
            if (!WindowsExternal.ReadProcessMemory(_handle, IntPtr.Add(patternAddress, 3), offsetBuffer, sizeof(int), out _))
            {
                _log.Info($"Failed to find pattern {pattern}");
                return IntPtr.Zero;
            }

            var offsetAddressToInt = BitConverter.ToInt32(offsetBuffer, 0);
            var delta = patternAddress.ToInt64() - _baseAddr.ToInt64();
            return IntPtr.Add(_baseAddr, (int)(delta - 0x121 + offsetAddressToInt));
        }

        public IntPtr GetMenuDataOffset(byte[] buffer)
        {
            var pattern = new Pattern("48 8D 0D ? ? ? ? 0F B6 04 08 C3");
            var patternAddress = FindPattern(buffer, pattern);
            if (patternAddress == IntPtr.Zero)
            {
                _log.Info($"Failed to find pattern {pattern}");
                return IntPtr.Zero;
            }

            var offsetBuffer = new byte[4];
            if (!WindowsExternal.ReadProcessMemory(_handle, IntPtr.Add(patternAddress, 3), offsetBuffer, sizeof(int), out _))
            {
                _log.Info($"Failed to read displacement for pattern {pattern}");
                return IntPtr.Zero;
            }

            var displacement = BitConverter.ToInt32(offsetBuffer, 0);
            return IntPtr.Add(patternAddress, 7 + displacement);
        }

        public IntPtr GetRosterDataOffset(byte[] buffer)
        {
            var pattern = new Pattern("02 45 33 D2 4D 8B");
            var patternAddress = FindPattern(buffer, pattern);
            if (patternAddress == IntPtr.Zero) return IntPtr.Zero;

            var offsetBuffer = new byte[4];
            if (!WindowsExternal.ReadProcessMemory(_handle, IntPtr.Add(patternAddress, -3), offsetBuffer, sizeof(int), out _))
            {
                _log.Info($"Failed to find pattern {pattern}");
                return IntPtr.Zero;
            }

            var offsetAddressToInt = BitConverter.ToInt32(offsetBuffer, 0);
            var delta = patternAddress.ToInt64() - _baseAddr.ToInt64();
            return IntPtr.Add(_baseAddr, (int)(delta + 1 + offsetAddressToInt));
        }

        public IntPtr GetInteractedNpcOffset(byte[] buffer)
        {
            var pattern = new Pattern("43 01 84 31 ? ? ? ?");
            var patternAddress = FindPattern(buffer, pattern);
            if (patternAddress == IntPtr.Zero) return IntPtr.Zero;

            var offsetBuffer = new byte[4];
            if (!WindowsExternal.ReadProcessMemory(_handle, IntPtr.Add(patternAddress, 4), offsetBuffer, sizeof(int), out _))
            {
                _log.Info($"Failed to find pattern {pattern}");
                return IntPtr.Zero;
            }

            return IntPtr.Add(_baseAddr, BitConverter.ToInt32(offsetBuffer, 0) + 0x1D4);
        }

        public IntPtr GetLastHoverObjectOffset(byte[] buffer)
        {
            var pattern = new Pattern("C6 84 C2 ? ? ? ? ? 48 8B 74 24");
            var patternAddress = FindPattern(buffer, pattern);
            if (patternAddress == IntPtr.Zero) return IntPtr.Zero;

            var offsetBuffer = new byte[4];
            if (!WindowsExternal.ReadProcessMemory(_handle, IntPtr.Add(patternAddress, 3), offsetBuffer, sizeof(int), out _))
            {
                _log.Info($"Failed to find pattern {pattern}");
                return IntPtr.Zero;
            }

            return IntPtr.Add(_baseAddr, BitConverter.ToInt32(offsetBuffer, 0) - 1);
        }

        public IntPtr GetPetsOffset(byte[] buffer)
        {
            var pattern = new Pattern("48 8B 05 ? ? ? ? 48 89 41 30 89 59 08");
            var patternAddress = FindPattern(buffer, pattern);
            if (patternAddress == IntPtr.Zero) return IntPtr.Zero;

            var offsetBuffer = new byte[4];
            if (!WindowsExternal.ReadProcessMemory(_handle, IntPtr.Add(patternAddress, 3), offsetBuffer, sizeof(int), out _))
            {
                _log.Info($"Failed to find pattern {pattern}");
                return IntPtr.Zero;
            }

            var displacement = BitConverter.ToInt32(offsetBuffer, 0);
            return IntPtr.Add(patternAddress, 7 + displacement);
        }

        public byte[] ReadModuleBuffer(IntPtr address, int size)
        {
            const int pageSize = 0x1000;
            const long pageMask = pageSize - 1;
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));

            var buffer = new byte[size];
            if (size == 0) return buffer;

            var totalRead = 0;
            var offset = 0;
            while (offset < size)
            {
                var currentAddress = IntPtr.Add(address, offset);
                var offsetWithinPage = (int)(currentAddress.ToInt64() & pageMask);
                var bytesRemainingInPage = pageSize - offsetWithinPage;
                var bytesToRead = Math.Min(bytesRemainingInPage, size - offset);
                var pageBuffer = new byte[bytesToRead];

                var success = WindowsExternal.ReadProcessMemory(_handle, currentAddress, pageBuffer, bytesToRead, out var bytesRead);
                var countRead = (int)Math.Max(0, Math.Min(bytesRead.ToInt64(), bytesToRead));
                if (success || countRead > 0)
                {
                    Buffer.BlockCopy(pageBuffer, 0, buffer, offset, countRead);
                    totalRead += countRead;
                }

                offset += bytesToRead;
            }

            _log.Info($"Paged memory read complete. Address=0x{address.ToInt64():X}, Requested={size:N0}, Read={totalRead:N0}, Unread={size - totalRead:N0}");
            return buffer;
        }

        public T[] ReadFull<T>(IntPtr address, int count) where T : struct
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var sz = Marshal.SizeOf<T>();
            var buf = ReadModuleBuffer(address, checked(sz * count));
            return BytesToStructArray<T>(buf, count, sz);
        }

        public T[] Read<T>(IntPtr address, int count) where T : struct
        {
            var sz = Marshal.SizeOf<T>();
            var buf = new byte[sz * count];
            WindowsExternal.ReadProcessMemory(_handle, address, buf, buf.Length, out _);
            return BytesToStructArray<T>(buf, count, sz);
        }

        private static T[] BytesToStructArray<T>(byte[] buf, int count, int structSize) where T : struct
        {
            var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                var result = new T[count];
                if (structSize == 1)
                {
                    Buffer.BlockCopy(buf, 0, result, 0, buf.Length);
                    return result;
                }

                for (var i = 0; i < count; i++)
                {
                    result[i] = (T)Marshal.PtrToStructure(IntPtr.Add(handle.AddrOfPinnedObject(), i * structSize), typeof(T));
                }

                return result;
            }
            finally
            {
                handle.Free();
            }
        }

        public T Read<T>(IntPtr address) where T : struct => Read<T>(address, 1)[0];

        public IntPtr FindPattern(byte[] buffer, Pattern pattern)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                if (pattern.Match(buffer, i)) return IntPtr.Add(_baseAddr, i);
            }

            return IntPtr.Zero;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue) return;
            _disposedValue = true;

            if (_handle != IntPtr.Zero)
            {
                WindowsExternal.CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (--OpenContextCount > 0) return;
            Dispose(disposing: true);
        }
    }
}
