using System;
using System.Runtime.InteropServices;

namespace signpost_forwarder
{
	public static class Signposter
	{
        [DllImport("libsignposter.dynlib", EntryPoint = "create_log_handle")]
        internal static extern IntPtr CreateLogHandle(string subsystem_name);

        [DllImport("libsignposter.dynlib", EntryPoint = "generate_signpost_id")]
        internal static extern ulong GenerateSignpostID(IntPtr log_handle);

        [DllImport("libsignposter.dynlib", EntryPoint = "emit_signpost_event")]
        internal static extern void EmitSignpostEvent(IntPtr log_handle, ulong signpost_id, string payload);
    }
}

