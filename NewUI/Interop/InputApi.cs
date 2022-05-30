﻿using Mesen.Config;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Mesen.Interop
{
	public class InputApi
	{
		private const string DllPath = "MesenSCore.dll";
		
		[DllImport(DllPath)] public static extern void SetKeyState(Int32 scanCode, [MarshalAs(UnmanagedType.I1)]bool pressed);
		[DllImport(DllPath)] public static extern void ResetKeyState();

		[DllImport(DllPath)] public static extern void SetMouseMovement(Int16 x, Int16 y);
		[DllImport(DllPath)] public static extern void SetMousePosition(double x, double y);
		[DllImport(DllPath)] public static extern void DisableAllKeys([MarshalAs(UnmanagedType.I1)]bool disabled);
		[DllImport(DllPath)] public static extern void UpdateInputDevices();

		[DllImport(DllPath)] public static extern UInt32 GetKeyCode([MarshalAs(UnmanagedType.LPUTF8Str)]string keyName);
		
		[DllImport(DllPath)][return: MarshalAs(UnmanagedType.I1)] public static extern bool HasControlDevice(ControllerType type);

		[DllImport(DllPath, EntryPoint = "GetKeyName")] private static extern IntPtr GetKeyNameWrapper(UInt32 key, IntPtr outKeyName, Int32 maxLength);
		public unsafe static string GetKeyName(UInt32 key)
		{
			byte[] outKeyName = new byte[1000];
			fixed(byte* ptr = outKeyName) {
				InputApi.GetKeyNameWrapper(key, (IntPtr)ptr, outKeyName.Length);
				return Utf8Utilities.PtrToStringUtf8((IntPtr)ptr);
			}
		}

		[DllImport(DllPath, EntryPoint = "GetPressedKeys")] private static extern void GetPressedKeysWrapper(IntPtr keyBuffer);
		public static List<UInt32> GetPressedKeys()
		{
			UInt32[] keyBuffer = new UInt32[3];
			GCHandle handle = GCHandle.Alloc(keyBuffer, GCHandleType.Pinned);
			try {
				InputApi.GetPressedKeysWrapper(handle.AddrOfPinnedObject());
			} finally {
				handle.Free();
			}

			List<UInt32> keys = new List<UInt32>();
			for(int i = 0; i < 3; i++) {
				if(keyBuffer[i] != 0) {
					keys.Add(keyBuffer[i]);
				}
			}
			return keys;
		}
	}
}
